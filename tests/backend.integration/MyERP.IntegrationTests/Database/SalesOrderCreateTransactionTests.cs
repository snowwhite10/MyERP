using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Domain;
using MyERP.Modules.MasterData.Infrastructure;
using Moq;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Sales;
using MyERP.Modules.Sales.Application;
using MyERP.Modules.Sales.Domain;
using MyERP.Modules.Sales.Infrastructure;
using MyERP.Modules.System.Application.Numbering;
using MyERP.Modules.System.Domain;
using MyERP.Modules.System.Infrastructure;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Monetary;
using MyERP.Shared.Numbering;
using MyERP.Shared.Paging;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests.Database;

public sealed class SalesOrderCreateTransactionTests(TestDatabase database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateOnly Day = new(2028, 2, 29);

    [Fact]
    public async Task EditingOneOrderDoesNotLockAnotherOrdersLineInsertRange()
    {
        long companyId;
        await using (var setup = Context(null))
        {
            var company = new SystemCompany { Code = "SX" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
                Name = "Independent order line locks" };
            setup.Add(company);
            await setup.SaveChangesAsync(Ct);
            companyId = company.Id;
        }

        static SalesOrder Order(long company, string number) => new()
        {
            CompanyId = company, DocumentNo = number, CustomerId = 21, CustomerCode = "C21",
            CustomerName = "Customer", BusinessDate = Day, CurrencyCode = "TWD",
            BaseCurrencyCode = "TWD", ExchangeRate = 1m, OwnerUserId = 7,
            OriginalNet = 10m, OriginalGross = 10m, BaseNet = 10m, BaseGross = 10m
        };
        static SalesOrderLine Line(long company, int lineNo) => new()
        {
            CompanyId = company, LineNo = lineNo, ItemId = 31, ItemCode = "I31", ItemName = "Item",
            TaxRateId = 41, Quantity = 1m, UnitPrice = 10m, OriginalNet = 10m, OriginalGross = 10m,
            BaseNet = 10m, BaseGross = 10m
        };

        long editedId, insertedId;
        await using (var setup = Context(companyId))
        {
            var edited = Order(companyId, "SX" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant());
            var inserted = Order(companyId, "SY" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant());
            edited.Lines.Add(Line(companyId, 1));
            inserted.Lines.Add(Line(companyId, 1));
            setup.AddRange(edited, inserted);
            await setup.SaveChangesAsync(Ct);
            editedId = edited.Id;
            insertedId = inserted.Id;
        }

        await using var edit = Context(companyId);
        await using var insert = Context(companyId);
        await edit.Database.BeginTransactionAsync(IsolationLevel.Serializable, Ct);
        var replacement = Order(companyId, "unused");
        replacement.Remark = "Edited while another order grows";
        replacement.Lines.Add(Line(companyId, 1));
        await new SalesOrderDraftStore(edit, new FixedClock(new DateTime(2026, 10, 27)))
            .ReplaceAsync(editedId, 1, replacement, Ct);

        // The first transaction stays open while a separate connection inserts into
        // another header. A clustered line scan under Serializable blocks this insert.
        await insert.Database.OpenConnectionAsync(Ct);
        await insert.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT 3000", Ct);
        try
        {
            insert.Add(new SalesOrderLine
            {
                CompanyId = companyId, OrderId = insertedId, LineNo = 2,
                ItemId = 32, ItemCode = "I32", ItemName = "Second item",
                TaxRateId = 41, Quantity = 1m, UnitPrice = 10m, OriginalNet = 10m, OriginalGross = 10m,
                BaseNet = 10m, BaseGross = 10m
            });
            await insert.SaveChangesAsync(Ct);
        }
        finally
        {
            await insert.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT -1", Ct);
        }
        await edit.Database.CommitTransactionAsync(Ct);

        await using var verify = Context(companyId);
        var editedSaved = await verify.Set<SalesOrder>().Include(x => x.Lines)
            .SingleAsync(x => x.Id == editedId, Ct);
        var insertedSaved = await verify.Set<SalesOrder>().Include(x => x.Lines)
            .SingleAsync(x => x.Id == insertedId, Ct);
        Assert.Equal(2, editedSaved.Version);
        Assert.Equal("Edited while another order grows", editedSaved.Remark);
        Assert.Single(editedSaved.Lines);
        Assert.Collection(insertedSaved.Lines.OrderBy(x => x.LineNo),
            line => Assert.Equal(1, line.LineNo), line => Assert.Equal(2, line.LineNo));
    }

    [Fact]
    public async Task OrderListUsesViewScopeAndStablePages()
    {
        long companyId;
        await using (var setup = Context(null))
        {
            var company = new SystemCompany { Code = "SL" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
                Name = "Scoped order list" };
            setup.Add(company);
            await setup.SaveChangesAsync(Ct);
            companyId = company.Id;
        }
        await using var create = Context(companyId);
        create.Add(new NumberRule { CompanyId = companyId, DocumentType = "SAL.ORDER", Name = "Sales order",
            Prefix = "SL", DateFormat = NumberDateFormat.None, ResetPeriod = NumberResetPeriod.Never,
            SequenceLength = 3 });
        await create.SaveChangesAsync(Ct);
        var input = new SalesOrderDraftInput(21, Day, "TWD", null,
            [new SalesOrderLineInput(1, 31, 1m, 10m, 0m, 41)]);
        var first = await Service(create, companyId, createScope: DataScope.Branch, branchId: 55)
            .CreateAsync(input, Ct);
        var second = await Service(create, companyId, createScope: DataScope.Branch, branchId: 55)
            .CreateAsync(input, Ct);
        await using var read = Context(companyId);
        SalesOrderQueryService Query(long? branchId)
        {
            var auth = new Mock<ICurrentAuthorization>();
            auth.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
                new UserCompanyAuthorization(8, companyId, branchId, null, false,
                    new Dictionary<string, DataScope> { ["SAL.ORDER.VIEW"] = DataScope.Branch }));
            var company = new SalesCompanyAccess(new FixedCompany(companyId));
            return new SalesOrderQueryService(read, new SalesActionAccess(company, auth.Object), company);
        }
        var page1 = await Query(55).ListAsync(new SalesOrderQuery { PageSize = 1 }, Ct);
        var page2 = await Query(55).ListAsync(new SalesOrderQuery { Page = 2, PageSize = 1 }, Ct);
        Assert.Equal(2, page1.TotalCount);
        Assert.Equal(second.Id, Assert.Single(page1.Items).Id);
        Assert.Equal(first.Id, Assert.Single(page2.Items).Id);
        Assert.Empty((await Query(99).ListAsync(new SalesOrderQuery(), Ct)).Items);
        await Assert.ThrowsAsync<MyERP.Shared.Errors.RequestValidationException>(() =>
            Query(55).ListAsync(new SalesOrderQuery { Sort = "customerName" }, Ct));

        second.BusinessDate = Day.AddDays(1);
        second.CustomerId = 22;
        second.CustomerCode = "C22";
        second.CustomerName = "Other customer";
        second.Status = SalesOrderStatus.Pending;
        await create.SaveChangesAsync(Ct);
        await Service(create, companyId, createScope: DataScope.Branch, branchId: 99)
            .CreateAsync(input, Ct);

        async Task AssertOnlyAsync(SalesOrderQuery filter, long expectedId)
        {
            var page = await Query(55).ListAsync(filter, Ct);
            Assert.Equal(1, page.TotalCount);
            Assert.Equal(expectedId, Assert.Single(page.Items).Id);
        }
        await AssertOnlyAsync(new SalesOrderQuery { From = Day.AddDays(1) }, second.Id);
        await AssertOnlyAsync(new SalesOrderQuery { To = Day }, first.Id);
        await AssertOnlyAsync(new SalesOrderQuery { Status = SalesOrderStatus.Pending }, second.Id);
        await AssertOnlyAsync(new SalesOrderQuery { CustomerId = 22 }, second.Id);
        await AssertOnlyAsync(new SalesOrderQuery { Keyword = "  C22  " }, second.Id);
        await AssertOnlyAsync(new SalesOrderQuery { Keyword = first.DocumentNo }, first.Id);
        foreach (var invalid in new[]
        {
            new SalesOrderQuery { Page = 0 },
            new SalesOrderQuery { PageSize = 0 },
            new SalesOrderQuery { PageSize = 201 },
            new SalesOrderQuery { Page = int.MaxValue, PageSize = 200 },
            new SalesOrderQuery { From = Day.AddDays(1), To = Day },
            new SalesOrderQuery { CustomerId = 0 },
            new SalesOrderQuery { Status = (SalesOrderStatus)99 },
            new SalesOrderQuery { Sort = "documentNo,-businessDate" }
        })
            await Assert.ThrowsAsync<MyERP.Shared.Errors.RequestValidationException>(() =>
                Query(55).ListAsync(invalid, Ct));
    }

    [Fact]
    public async Task DraftCommandsUseTheirOwnActionScopeAndRejectStaleVersions()
    {
        long companyId;
        await using (var setup = Context(null))
        {
            var company = new SystemCompany { Code = "SE" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
                Name = "Scoped order edit" };
            setup.Add(company);
            await setup.SaveChangesAsync(Ct);
            companyId = company.Id;
        }
        await using var db = Context(companyId);
        db.Add(new NumberRule { CompanyId = companyId, DocumentType = "SAL.ORDER", Name = "Sales order",
            Prefix = "SE", DateFormat = NumberDateFormat.None, ResetPeriod = NumberResetPeriod.Never,
            SequenceLength = 3 });
        await db.SaveChangesAsync(Ct);
        var input = new SalesOrderDraftInput(21, Day, "TWD", null,
            [new SalesOrderLineInput(1, 31, 1m, 10m, 0m, 41)]);
        var saved = await Service(db, companyId, createScope: DataScope.Branch, branchId: 55,
            departmentId: 66).CreateAsync(input, Ct);
        await using var editDb = Context(companyId);
        SalesOrderDraftCommandService Command(DataScope editScope, DataScope deleteScope, long? branchId)
        {
            var auth = new Mock<ICurrentAuthorization>();
            auth.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
                new UserCompanyAuthorization(7, companyId, branchId, 66, false,
                    new Dictionary<string, DataScope> { ["SAL.ORDER.EDIT"] = editScope,
                        ["SAL.ORDER.DELETE"] = deleteScope }));
            var customer = new Mock<ICustomerLookup>();
            customer.Setup(x => x.RequireApprovedAsync(21, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CustomerIdentity(21, "C21", "Customer"));
            var item = new Mock<ISalesItemLookup>();
            item.Setup(x => x.RequireActiveAsync(31, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SalesItemIdentity(31, "I31", "Item"));
            var currency = new Mock<ICurrencyLookup>();
            currency.Setup(x => x.FindCurrencyAsync("TWD", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CurrencyInfo("TWD", "Dollar", null, 0, true, true));
            currency.Setup(x => x.GetBaseCurrencyAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CurrencyInfo("TWD", "Dollar", null, 0, true, true));
            currency.Setup(x => x.GetRateSnapshotAsync("TWD", Day, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ExchangeRateSnapshot(1m, Day, null, null));
            currency.Setup(x => x.GetPrecisionAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(CurrencyPrecision.Default);
            var tax = new Mock<ITaxRateLookup>();
            tax.Setup(x => x.GetRateSnapshotAsync(41, TaxDirection.Output, Day, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TaxRateSnapshot(41, 5m, Day, null));
            var company = new SalesCompanyAccess(new FixedCompany(companyId));
            return new SalesOrderDraftCommandService(editDb, new SalesActionAccess(company, auth.Object), company,
                new SalesOrderSnapshotBuilder(customer.Object, item.Object, currency.Object, tax.Object),
                new SalesOrderDraftStore(editDb, new FixedClock(new DateTime(2026, 10, 27))));
        }
        var hidden = Command(DataScope.Branch, DataScope.Branch, 99);
        await Assert.ThrowsAsync<MyERP.Shared.Errors.NotFoundException>(() =>
            hidden.ReplaceAsync(saved.Id, 1, input, Ct));
        await Assert.ThrowsAsync<MyERP.Shared.Errors.NotFoundException>(() =>
            hidden.DeleteAsync(saved.Id, 1, Ct));
        var command = Command(DataScope.Branch, DataScope.Self, 55);
        var revised = await command.ReplaceAsync(saved.Id, 1, input with { Remark = "Revised" }, Ct);
        Assert.Equal(2, revised.Version);
        Assert.Equal("Revised", revised.Remark);
        await Assert.ThrowsAsync<MyERP.Shared.Errors.ConflictException>(() =>
            command.DeleteAsync(saved.Id, 1, Ct));
        await command.DeleteAsync(saved.Id, 2, Ct);
        await using var verify = Context(companyId);
        Assert.False(await verify.Set<SalesOrder>().AnyAsync(x => x.Id == saved.Id, Ct));
    }

    [Fact]
    public async Task OrderDetailUsesViewActionScopeBeforeRevealingSavedLines()
    {
        long companyId;
        await using (var setup = Context(null))
        {
            var company = new SystemCompany { Code = "SV" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
                Name = "Scoped order view" };
            setup.Add(company);
            await setup.SaveChangesAsync(Ct);
            companyId = company.Id;
        }
        await using var create = Context(companyId);
        create.Add(new NumberRule { CompanyId = companyId, DocumentType = "SAL.ORDER", Name = "Sales order",
            Prefix = "SV", DateFormat = NumberDateFormat.None, ResetPeriod = NumberResetPeriod.Never,
            SequenceLength = 3 });
        await create.SaveChangesAsync(Ct);
        var saved = await Service(create, companyId, createScope: DataScope.Branch, branchId: 55,
            departmentId: 66).CreateAsync(new SalesOrderDraftInput(21, Day, "TWD", null,
            [new SalesOrderLineInput(1, 31, 1m, 10m, 0m, 41)]), Ct);
        await using var read = Context(companyId);
        SalesOrderQueryService Query(DataScope scope, long userId, long? branchId, long? departmentId)
        {
            var auth = new Mock<ICurrentAuthorization>();
            auth.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
                new UserCompanyAuthorization(userId, companyId, branchId, departmentId, false,
                    new Dictionary<string, DataScope> { ["SAL.ORDER.VIEW"] = scope }));
            var company = new SalesCompanyAccess(new FixedCompany(companyId));
            return new SalesOrderQueryService(read, new SalesActionAccess(company, auth.Object), company);
        }
        Assert.Single((await Query(DataScope.Branch, 8, 55, null).GetAsync(saved.Id, Ct)).Lines);
        await Assert.ThrowsAsync<MyERP.Shared.Errors.NotFoundException>(() =>
            Query(DataScope.Department, 8, 55, 67).GetAsync(saved.Id, Ct));
        Assert.Single((await Query(DataScope.Self, 7, null, null).GetAsync(saved.Id, Ct)).Lines);
        await Assert.ThrowsAsync<MyERP.Shared.Errors.NotFoundException>(() =>
            Query(DataScope.Self, 8, null, null).GetAsync(saved.Id, Ct));
    }

    [Fact]
    public async Task ManualCreatePersistsOwnerAndOrganizationForActionScope()
    {
        long companyId;
        await using (var setup = Context(null))
        {
            var company = new SystemCompany { Code = "SC" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
                Name = "Scoped order test" };
            setup.Add(company);
            await setup.SaveChangesAsync(Ct);
            companyId = company.Id;
        }
        await using var db = Context(companyId);
        db.Add(new NumberRule { CompanyId = companyId, DocumentType = "SAL.ORDER", Name = "Sales order",
            Prefix = "SC", DateFormat = NumberDateFormat.None, ResetPeriod = NumberResetPeriod.Never,
            SequenceLength = 3 });
        await db.SaveChangesAsync(Ct);
        var saved = await Service(db, companyId, createScope: DataScope.Branch, branchId: 55,
            departmentId: 66).CreateAsync(new SalesOrderDraftInput(21, Day, "TWD", null,
            [new SalesOrderLineInput(1, 31, 1m, 10m, 0m, 41)]), Ct);
        await using var verify = Context(companyId);
        Assert.Equal(55, saved.BranchId);
        Assert.Equal(66, saved.DepartmentId);
        Assert.Equal(7, saved.OwnerUserId);
        Assert.True(await new DataScopeFilter(DataScope.Branch, 8, 55, null)
            .Apply(verify.Set<SalesOrder>()).AnyAsync(x => x.Id == saved.Id, Ct));
        Assert.False(await new DataScopeFilter(DataScope.Department, 8, 55, 67)
            .Apply(verify.Set<SalesOrder>()).AnyAsync(x => x.Id == saved.Id, Ct));
        Assert.True(await new DataScopeFilter(DataScope.Self, 7, null, null)
            .Apply(verify.Set<SalesOrder>()).AnyAsync(x => x.Id == saved.Id, Ct));
    }

    [Fact]
    public async Task ManualCommandPersistsSnapshotAndFailedSaveReturnsSysNumber()
    {
        long companyId;
        await using (var setup = Context(null))
        {
            var company = new SystemCompany { Code = "SO" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
                Name = "Sales order test" };
            setup.Add(company);
            await setup.SaveChangesAsync(Ct);
            companyId = company.Id;
        }
        await using var db = Context(companyId);
        db.Add(new NumberRule { CompanyId = companyId, DocumentType = "SAL.ORDER", Name = "Sales order",
            Prefix = "SO", DateFormat = NumberDateFormat.None, ResetPeriod = NumberResetPeriod.Never,
            SequenceLength = 3 });
        await db.SaveChangesAsync(Ct);
        var command = Service(db, companyId);
        var input = new SalesOrderDraftInput(21, Day, "TWD", null,
            [new SalesOrderLineInput(1, 31, 2m, 10m, 0m, 41)]);

        var saved = await command.CreateAsync(input, Ct);
        Assert.Equal("SO001", saved.DocumentNo);
        Assert.Equal(21m, saved.OriginalGross);
        Assert.Equal(21m, Assert.Single(saved.Lines).OriginalGross);

        // The sequence advances, but a simulated downstream duplicate document insert fails.
        var failed = Service(db, companyId, new DuplicateAfterAllocation(
            new NumberGenerator(db, new FixedClock(new DateTime(2026, 10, 27)), new FixedUser(7))));
        await Assert.ThrowsAsync<DbUpdateException>(() => failed.CreateAsync(input, Ct));
        await Assert.ThrowsAsync<MyERP.Shared.Errors.ForbiddenException>(() =>
            Service(db, companyId, allowCreate: false).CreateAsync(input, Ct));
        await using var verify = Context(companyId);
        Assert.Equal(1, await verify.Set<SalesOrder>().CountAsync(Ct));
        Assert.Equal(1, (await verify.Set<NumberSequence>().SingleAsync(Ct)).CurrentValue);
    }

    [Fact]
    public async Task ConcurrentManualCreatesWaitForCommittedSysNumberAndRemainDistinct()
    {
        long companyId;
        await using (var setup = Context(null))
        {
            var company = new SystemCompany { Code = "SO" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
                Name = "Sales numbering race test" };
            setup.Add(company);
            await setup.SaveChangesAsync(Ct);
            companyId = company.Id;
        }
        await using var first = Context(companyId);
        first.Add(new NumberRule { CompanyId = companyId, DocumentType = "SAL.ORDER", Name = "Sales order",
            Prefix = "SO", DateFormat = NumberDateFormat.None, ResetPeriod = NumberResetPeriod.Never,
            SequenceLength = 3 });
        await first.SaveChangesAsync(Ct);
        await using var second = Context(companyId);
        await second.Database.OpenConnectionAsync(Ct);
        var secondNumbers = new NumberGenerator(second, new FixedClock(new DateTime(2026, 10, 27)), new FixedUser(7));
        var firstNumbers = new NumberGenerator(first, new FixedClock(new DateTime(2026, 10, 27)), new FixedUser(7));
        var blocked = false;
        var probe = new AfterAllocation(firstNumbers, async token =>
        {
            await second.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT 1000", token);
            try
            {
                var error = await Assert.ThrowsAsync<SqlException>(() => secondNumbers.NextAsync("SAL.ORDER", Day, token));
                Assert.Equal(1222, error.Number);
                blocked = true;
            }
            finally
            {
                await second.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT -1", token);
            }
        });
        var input = new SalesOrderDraftInput(21, Day, "TWD", null,
            [new SalesOrderLineInput(1, 31, 2m, 10m, 0m, 41)]);
        var savedFirst = await Service(first, companyId, numbers: probe).CreateAsync(input, Ct);
        Assert.True(blocked);
        var savedSecond = await Service(second, companyId, numbers: secondNumbers).CreateAsync(input, Ct);
        Assert.Equal("SO001", savedFirst.DocumentNo);
        Assert.Equal("SO002", savedSecond.DocumentNo);
        await using var verify = Context(companyId);
        Assert.Equal(2, await verify.Set<SalesOrder>().CountAsync(Ct));
        Assert.Equal(2, (await verify.Set<NumberSequence>().SingleAsync(Ct)).CurrentValue);
    }

    [Fact]
    public async Task ManualCommandHoldsRealMdIdentityStableUntilCommit()
    {
        long companyId;
        await using (var setup = Context(null))
        {
            var company = new SystemCompany { Code = "SO" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
                Name = "Sales MD stability test" };
            setup.Add(company);
            await setup.SaveChangesAsync(Ct);
            companyId = company.Id;
        }
        await using var db = Context(companyId);
        db.Add(new NumberRule { CompanyId = companyId, DocumentType = "SAL.ORDER", Name = "Sales order",
            Prefix = "SO", DateFormat = NumberDateFormat.None, ResetPeriod = NumberResetPeriod.Never,
            SequenceLength = 3 });
        await db.SaveChangesAsync(Ct);
        var uom = await new UomService(db).CreateAsync("U" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), "Unit", 0, Ct);
        var item = await new ItemService(db).CreateAsync("I" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
            "Original item", null, uom.Id, null, null, Ct);
        var realItem = new SalesItemLookup(db);
        var observedIsolation = IsolationLevel.Unspecified;
        var itemLookup = new Mock<ISalesItemLookup>();
        itemLookup.Setup(x => x.RequireActiveAsync(item.Id, It.IsAny<CancellationToken>()))
            .Returns(async (long id, CancellationToken token) =>
            {
                observedIsolation = db.Database.CurrentTransaction!.GetDbTransaction().IsolationLevel;
                var identity = await realItem.RequireActiveAsync(id, token);
                await using var other = new SqlConnection(database.ConnectionString);
                await other.OpenAsync(token);
                await using var update = other.CreateCommand();
                update.CommandText = "SET LOCK_TIMEOUT 1000; UPDATE dbo.md_item SET name = N'Changed item' WHERE id = @id";
                update.Parameters.Add(new SqlParameter("@id", SqlDbType.BigInt) { Value = id });
                var blocked = await Assert.ThrowsAsync<SqlException>(() => update.ExecuteNonQueryAsync(token));
                Assert.Equal(1222, blocked.Number);
                return identity;
            });
        var saved = await Service(db, companyId, items: itemLookup.Object).CreateAsync(
            new SalesOrderDraftInput(21, Day, "TWD", null,
                [new SalesOrderLineInput(1, item.Id, 2m, 10m, 0m, 41)]), Ct);
        Assert.Equal(IsolationLevel.Serializable, observedIsolation);
        Assert.Equal("Original item", Assert.Single(saved.Lines).ItemName);
        await using var after = new SqlConnection(database.ConnectionString);
        await after.OpenAsync(Ct);
        await using var change = after.CreateCommand();
        change.CommandText = "UPDATE dbo.md_item SET name = N'Changed item' WHERE id = @id";
        change.Parameters.Add(new SqlParameter("@id", SqlDbType.BigInt) { Value = item.Id });
        Assert.Equal(1, await change.ExecuteNonQueryAsync(Ct));
        await using var verify = Context(companyId);
        Assert.Equal("Original item", await verify.Set<SalesOrderLine>().Where(x => x.OrderId == saved.Id)
            .Select(x => x.ItemName).SingleAsync(Ct));
    }

    [Fact]
    public async Task ManualCommandHoldsApprovedCustomerStableUntilCommit()
    {
        long companyId;
        await using (var setup = Context(null))
        {
            var company = new SystemCompany { Code = "SO" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
                Name = "Sales customer stability test" };
            setup.Add(company);
            await setup.SaveChangesAsync(Ct);
            companyId = company.Id;
        }
        await using var db = Context(companyId);
        db.Add(new NumberRule { CompanyId = companyId, DocumentType = "SAL.ORDER", Name = "Sales order",
            Prefix = "SO", DateFormat = NumberDateFormat.None, ResetPeriod = NumberResetPeriod.Never,
            SequenceLength = 3 });
        var customer = new Customer { CompanyId = companyId, Code = "C" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
            Name = "Original customer", ReviewStatus = PartnerReviewStatus.Approved };
        db.Add(customer);
        await db.SaveChangesAsync(Ct);

        var realCustomer = new CustomerService(db);
        var customerLookup = new Mock<ICustomerLookup>();
        customerLookup.Setup(x => x.RequireApprovedAsync(customer.Id, It.IsAny<CancellationToken>()))
            .Returns(async (long id, CancellationToken token) =>
            {
                var identity = await realCustomer.RequireApprovedAsync(id, token);
                await using var other = new SqlConnection(database.ConnectionString);
                await other.OpenAsync(token);
                await using var update = other.CreateCommand();
                update.CommandText = "SET LOCK_TIMEOUT 1000; UPDATE dbo.md_customer SET name = N'Changed customer' WHERE id = @id";
                update.Parameters.Add(new SqlParameter("@id", SqlDbType.BigInt) { Value = id });
                var blocked = await Assert.ThrowsAsync<SqlException>(() => update.ExecuteNonQueryAsync(token));
                Assert.Equal(1222, blocked.Number);
                return identity;
            });

        var saved = await Service(db, companyId, customers: customerLookup.Object).CreateAsync(
            new SalesOrderDraftInput(customer.Id, Day, "TWD", null,
                [new SalesOrderLineInput(1, 31, 2m, 10m, 0m, 41)]), Ct);
        Assert.Equal("Original customer", saved.CustomerName);
        await using var after = new SqlConnection(database.ConnectionString);
        await after.OpenAsync(Ct);
        await using var change = after.CreateCommand();
        change.CommandText = "UPDATE dbo.md_customer SET name = N'Changed customer' WHERE id = @id";
        change.Parameters.Add(new SqlParameter("@id", SqlDbType.BigInt) { Value = customer.Id });
        Assert.Equal(1, await change.ExecuteNonQueryAsync(Ct));
        await using var verify = Context(companyId);
        Assert.Equal("Original customer", await verify.Set<SalesOrder>().Where(x => x.Id == saved.Id)
            .Select(x => x.CustomerName).SingleAsync(Ct));
    }

    [Fact]
    public async Task ManualCommandHoldsCurrencyRateAndOutputTaxStableUntilCommit()
    {
        long companyId;
        await using (var setup = Context(null))
        {
            var company = new SystemCompany { Code = "SO" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
                Name = "Sales rate stability test" };
            setup.Add(company);
            await setup.SaveChangesAsync(Ct);
            companyId = company.Id;
        }
        await using var db = Context(companyId);
        db.Add(new NumberRule { CompanyId = companyId, DocumentType = "SAL.ORDER", Name = "Sales order",
            Prefix = "SO", DateFormat = NumberDateFormat.None, ResetPeriod = NumberResetPeriod.Never,
            SequenceLength = 3 });
        var baseCurrency = new Currency { CompanyId = companyId, Code = "TWD", Name = "Taiwan dollar",
            DecimalPlaces = 0, IsBase = true };
        var foreignCurrency = new Currency { CompanyId = companyId, Code = "USD", Name = "Original dollar",
            DecimalPlaces = 2 };
        var taxCode = new TaxCode { CompanyId = companyId, Code = "OUT", Name = "Output", TaxType = "Output" };
        db.AddRange(baseCurrency, foreignCurrency, taxCode);
        await db.SaveChangesAsync(Ct);
        var exchange = new ExchangeRate { CompanyId = companyId, CurrencyId = foreignCurrency.Id,
            EffectiveFrom = Day, Rate = 30m };
        var taxRate = new TaxRate { CompanyId = companyId, TaxCodeId = taxCode.Id,
            EffectiveFrom = Day, RatePercent = 5m };
        db.AddRange(exchange, taxRate);
        await db.SaveChangesAsync(Ct);

        var realCurrency = new CurrencyLookup(db);
        var currencyLookup = new Mock<ICurrencyLookup>();
        currencyLookup.Setup(x => x.FindCurrencyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async (string code, CancellationToken token) =>
            {
                var identity = await realCurrency.FindCurrencyAsync(code, token);
                await AssertUpdateBlockedAsync("UPDATE dbo.md_currency SET decimal_places = 3 WHERE id = @id",
                    foreignCurrency.Id, token);
                return identity;
            });
        currencyLookup.Setup(x => x.GetBaseCurrencyAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken token) => realCurrency.GetBaseCurrencyAsync(token));
        currencyLookup.Setup(x => x.GetPrecisionAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken token) => realCurrency.GetPrecisionAsync(token));
        currencyLookup.Setup(x => x.GetRateSnapshotAsync("USD", Day, It.IsAny<CancellationToken>()))
            .Returns(async (string code, DateOnly date, CancellationToken token) =>
            {
                var snapshot = await realCurrency.GetRateSnapshotAsync(code, date, token);
                await AssertUpdateBlockedAsync("UPDATE dbo.md_exchange_rate SET rate = 31 WHERE id = @id", exchange.Id, token);
                return snapshot;
            });
        var realTax = new TaxRateLookup(db, realCurrency);
        var taxLookup = new Mock<ITaxRateLookup>();
        taxLookup.Setup(x => x.GetRateSnapshotAsync(taxRate.Id, TaxDirection.Output, Day, It.IsAny<CancellationToken>()))
            .Returns(async (long id, TaxDirection direction, DateOnly date, CancellationToken token) =>
            {
                var snapshot = await realTax.GetRateSnapshotAsync(id, direction, date, token);
                await AssertUpdateBlockedAsync("UPDATE dbo.md_tax_rate SET rate_percent = 6 WHERE id = @id", id, token);
                await AssertUpdateBlockedAsync("UPDATE dbo.md_tax_code SET is_active = 0 WHERE id = @id", taxCode.Id, token);
                return snapshot;
            });

        var saved = await Service(db, companyId, currencies: currencyLookup.Object, taxes: taxLookup.Object).CreateAsync(
            new SalesOrderDraftInput(21, Day, "USD", null,
                [new SalesOrderLineInput(1, 31, 2m, 10m, 0m, taxRate.Id)]), Ct);
        Assert.Equal(30m, saved.ExchangeRate);
        Assert.Equal(5m, Assert.Single(saved.Lines).TaxRatePercent);
        await using var verify = Context(companyId);
        Assert.Equal(30m, await verify.Set<SalesOrder>().Where(x => x.Id == saved.Id)
            .Select(x => x.ExchangeRate).SingleAsync(Ct));
        Assert.Equal(5m, await verify.Set<SalesOrderLine>().Where(x => x.OrderId == saved.Id)
            .Select(x => x.TaxRatePercent).SingleAsync(Ct));
        await using var after = new SqlConnection(database.ConnectionString);
        await after.OpenAsync(Ct);
        await using var change = after.CreateCommand();
        change.CommandText = "UPDATE dbo.md_exchange_rate SET rate = 31 WHERE id = @id";
        change.Parameters.Add(new SqlParameter("@id", SqlDbType.BigInt) { Value = exchange.Id });
        Assert.Equal(1, await change.ExecuteNonQueryAsync(Ct));
        Assert.Equal(30m, await verify.Set<SalesOrder>().Where(x => x.Id == saved.Id)
            .Select(x => x.ExchangeRate).SingleAsync(Ct));
    }

    private async Task AssertUpdateBlockedAsync(string sql, long id, CancellationToken token)
    {
        await using var other = new SqlConnection(database.ConnectionString);
        await other.OpenAsync(token);
        await using var update = other.CreateCommand();
        update.CommandText = "SET LOCK_TIMEOUT 1000; " + sql;
        update.Parameters.Add(new SqlParameter("@id", SqlDbType.BigInt) { Value = id });
        var blocked = await Assert.ThrowsAsync<SqlException>(() => update.ExecuteNonQueryAsync(token));
        Assert.Equal(1222, blocked.Number);
    }

    private MyErpDbContext Context(long? companyId) => new(
        new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(new AuditStamper(
                new FixedClock(new DateTime(2026, 10, 27)), new FixedUser(7), new FixedCompany(companyId)))).Options,
        [new SalesOrderDbModel(), new SystemOrganizationDbModel(), new UomDbModel(), new ItemDbModel(),
            new CustomerDbModel(), new PaymentSetupDbModel(), new CurrencyDbModel(), new TaxDbModel()],
        new FixedCompany(companyId));

    private static SalesOrderCreateService Service(MyErpDbContext db, long companyId,
        INumberGenerator? numbers = null, bool allowCreate = true, ISalesItemLookup? items = null,
        ICustomerLookup? customers = null, ICurrencyLookup? currencies = null, ITaxRateLookup? taxes = null,
        DataScope? createScope = null, long? branchId = null, long? departmentId = null)
    {
        var auth = new Mock<ICurrentAuthorization>();
        auth.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            new UserCompanyAuthorization(7, companyId, branchId, departmentId,
                allowCreate && createScope is null,
                createScope is { } scope && allowCreate
                    ? new Dictionary<string, DataScope> { ["SAL.ORDER.CREATE"] = scope }
                    : new Dictionary<string, DataScope>()));
        var customer = new Mock<ICustomerLookup>();
        customer.Setup(x => x.RequireApprovedAsync(21, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CustomerIdentity(21, "C21", "Customer"));
        var item = new Mock<ISalesItemLookup>();
        item.Setup(x => x.RequireActiveAsync(31, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesItemIdentity(31, "I31", "Item"));
        var currency = new Mock<ICurrencyLookup>();
        currency.Setup(x => x.FindCurrencyAsync("TWD", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CurrencyInfo("TWD", "Dollar", null, 0, true, true));
        currency.Setup(x => x.GetBaseCurrencyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CurrencyInfo("TWD", "Dollar", null, 0, true, true));
        currency.Setup(x => x.GetRateSnapshotAsync("TWD", Day, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExchangeRateSnapshot(1m, Day, null, null));
        currency.Setup(x => x.GetPrecisionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CurrencyPrecision.Default);
        var tax = new Mock<ITaxRateLookup>();
        tax.Setup(x => x.GetRateSnapshotAsync(41, TaxDirection.Output, Day, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaxRateSnapshot(41, 5m, Day, null));
        var company = new SalesCompanyAccess(new FixedCompany(companyId));
        return new SalesOrderCreateService(db, new SalesActionAccess(company, auth.Object), company,
            numbers ?? new NumberGenerator(db, new FixedClock(new DateTime(2026, 10, 27)), new FixedUser(7)),
            new SalesOrderSnapshotBuilder(customers ?? customer.Object, items ?? item.Object,
                currencies ?? currency.Object, taxes ?? tax.Object),
            new SalesOrderDraftStore(db, new FixedClock(new DateTime(2026, 10, 27))));
    }

    private sealed class DuplicateAfterAllocation(INumberGenerator inner) : INumberGenerator
    {
        public async Task<string> NextAsync(string documentType, DateOnly documentDate,
            CancellationToken cancellationToken = default)
        {
            await inner.NextAsync(documentType, documentDate, cancellationToken);
            return "SO001";
        }
    }

    private sealed class AfterAllocation(INumberGenerator inner, Func<CancellationToken, Task> callback) : INumberGenerator
    {
        public async Task<string> NextAsync(string documentType, DateOnly documentDate,
            CancellationToken cancellationToken = default)
        {
            var number = await inner.NextAsync(documentType, documentDate, cancellationToken);
            await callback(cancellationToken);
            return number;
        }
    }
}
