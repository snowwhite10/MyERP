using System.Data;
using Microsoft.EntityFrameworkCore;
using Moq;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Sales;
using MyERP.Modules.Sales.Application;
using MyERP.Modules.Sales.Domain;
using MyERP.Modules.Sales.Infrastructure;
using MyERP.Shared.Approvals;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Monetary;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests.Database;

public sealed class SalesQuotationActionScopeTests(TestDatabase database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(ApprovalStatus.Pending, SalesQuotationStatus.Pending)]
    [InlineData(ApprovalStatus.Approved, SalesQuotationStatus.Approved)]
    public async Task SubmitRevalidatesReferencesAndSavesSysResultAtomically(
        ApprovalStatus approvalStatus, SalesQuotationStatus expected)
    {
        long id;
        await using (var create = Context(1))
        {
            await using var tx = await create.Database.BeginTransactionAsync(Ct);
            var draft = Draft("QS" + Guid.NewGuid().ToString("N")[..12]);
            draft.Remark = new string('R', 250);
            await new SalesQuotationDraftStore(create, new FixedClock(new DateTime(2026, 10, 29)))
                .CreateAsync(draft, Ct);
            await tx.CommitAsync(Ct);
            id = draft.Id;
        }

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
        var tax = new Mock<ITaxRateLookup>();
        tax.Setup(x => x.GetRateSnapshotAsync(41, TaxDirection.Output,
                new DateOnly(2028, 2, 29), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaxRateSnapshot(41, 0m, new DateOnly(2028, 2, 29), null));
        var workflow = new Mock<IApprovalWorkflowService>();
        workflow.Setup(x => x.SubmitAsync(It.IsAny<ApprovalSubmission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApprovalSubmission request, CancellationToken _) =>
                new ApprovalState(712, request.DocumentType, request.DocumentId, approvalStatus,
                    approvalStatus == ApprovalStatus.Pending ? 1 : null,
                    approvalStatus == ApprovalStatus.Pending ? 1 : 0));
        await using var db = Context(1);
        var service = new SalesQuotationSubmissionService(db,
            Access(1, 7, "SAL.QUOTATION.SUBMIT", DataScope.Self),
            new SalesCompanyAccess(new FixedCompany(1)), customer.Object, item.Object,
            currency.Object, tax.Object, workflow.Object,
            new FixedClock(new DateTime(2026, 10, 29)));
        var result = await service.SubmitAsync(id, 1, Ct);
        Assert.Equal(expected, result.Quotation.Status);
        Assert.Equal(712, result.Quotation.ApprovalInstanceId);
        Assert.NotNull(result.Quotation.SubmittedAt);
        Assert.Equal(approvalStatus == ApprovalStatus.Approved, result.Quotation.ApprovedAt.HasValue);
        await using var verify = Context(1);
        Assert.Equal(expected, (await verify.Set<SalesQuotation>()
            .SingleAsync(x => x.Id == id, Ct)).Status);
        workflow.Verify(x => x.SubmitAsync(It.Is<ApprovalSubmission>(s =>
            s.DocumentType == "SAL.QUOTATION" && s.DocumentId == id && s.Amount == 10m &&
            s.Summary != null && s.Summary.Length == 200),
            It.IsAny<CancellationToken>()), Times.Once);
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.SubmitAsync(id, 2, Ct));
    }

    [Fact]
    public async Task SubmitRefusesInvalidReferencesAndRollsBackFailedWorkflow()
    {
        long id;
        await using (var create = Context(1))
        {
            await using var tx = await create.Database.BeginTransactionAsync(Ct);
            var draft = Draft("QF" + Guid.NewGuid().ToString("N")[..12]);
            await new SalesQuotationDraftStore(create, new FixedClock(new DateTime(2026, 10, 29)))
                .CreateAsync(draft, Ct);
            await tx.CommitAsync(Ct);
            id = draft.Id;
        }
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
        var tax = new Mock<ITaxRateLookup>();
        tax.Setup(x => x.GetRateSnapshotAsync(41, TaxDirection.Output,
                new DateOnly(2028, 2, 29), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaxRateSnapshot(41, 0m, new DateOnly(2028, 2, 29), null));
        var workflow = new Mock<IApprovalWorkflowService>();
        workflow.Setup(x => x.SubmitAsync(It.IsAny<ApprovalSubmission>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessRuleException("SYS.APPROVAL.TEST_FAILURE", "SYS declined submission."));
        await using var db = Context(1);
        SalesQuotationSubmissionService Service(DataScope? scope) => new(db,
            Access(1, 7, "SAL.QUOTATION.SUBMIT", scope),
            new SalesCompanyAccess(new FixedCompany(1)), customer.Object, item.Object,
            currency.Object, tax.Object, workflow.Object,
            new FixedClock(new DateTime(2026, 10, 29)));

        await Assert.ThrowsAsync<ForbiddenException>(() => Service(null).SubmitAsync(id, 1, Ct));
        await Assert.ThrowsAsync<ConflictException>(() => Service(DataScope.Self).SubmitAsync(id, 2, Ct));
        customer.Setup(x => x.RequireApprovedAsync(21, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessRuleException("MD.CUSTOMER.NOT_APPROVED", "Customer is not approved."));
        await Assert.ThrowsAsync<BusinessRuleException>(() => Service(DataScope.Self).SubmitAsync(id, 1, Ct));
        workflow.Verify(x => x.SubmitAsync(It.IsAny<ApprovalSubmission>(), It.IsAny<CancellationToken>()), Times.Never);
        customer.Setup(x => x.RequireApprovedAsync(21, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CustomerIdentity(21, "C21", "Customer"));
        await Assert.ThrowsAsync<BusinessRuleException>(() => Service(DataScope.Self).SubmitAsync(id, 1, Ct));
        await using var verify = Context(1);
        var saved = await verify.Set<SalesQuotation>().SingleAsync(x => x.Id == id, Ct);
        Assert.Equal(SalesQuotationStatus.Draft, saved.Status);
        Assert.Null(saved.ApprovalInstanceId);
        Assert.Null(saved.SubmittedAt);
        Assert.Equal(1, saved.Version);
    }

    [Fact]
    public async Task ViewUsesItsOwnScopeAndNeverRevealsOtherCompanyOrOwner()
    {
        var number = "QV" + Guid.NewGuid().ToString("N")[..12];
        long id;
        await using (var create = Context(1))
        {
            await using var tx = await create.Database.BeginTransactionAsync(IsolationLevel.Serializable, Ct);
            var quote = Draft(number);
            await new SalesQuotationDraftStore(create, new FixedClock(new DateTime(2026, 10, 29)))
                .CreateAsync(quote, Ct);
            await tx.CommitAsync(Ct);
            id = quote.Id;
        }

        await using var db = Context(1);
        SalesQuotationQueryService Query(long companyId, long userId, DataScope? scope) =>
            new(db, Access(companyId, userId, "SAL.QUOTATION.VIEW", scope),
                new SalesCompanyAccess(new FixedCompany(companyId)));

        var visible = await Query(1, 7, DataScope.Self).GetAsync(id, Ct);
        Assert.Equal(number, visible.DocumentNo);
        Assert.Single(visible.Lines);
        await Assert.ThrowsAsync<NotFoundException>(() => Query(1, 8, DataScope.Self).GetAsync(id, Ct));
        await using var otherDb = Context(2);
        var otherCompany = new SalesCompanyAccess(new FixedCompany(2));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            new SalesQuotationQueryService(otherDb,
                Access(2, 7, "SAL.QUOTATION.VIEW", DataScope.Company), otherCompany).GetAsync(id, Ct));
        await Assert.ThrowsAsync<ForbiddenException>(() => Query(1, 7, null).GetAsync(id, Ct));
    }

    [Fact]
    public async Task EditUsesItsOwnScopeAndRebuildsSnapshotWithoutChangingNumberOrOwner()
    {
        var number = "QE" + Guid.NewGuid().ToString("N")[..12];
        long id;
        await using (var create = Context(1))
        {
            await using var tx = await create.Database.BeginTransactionAsync(IsolationLevel.Serializable, Ct);
            var quote = Draft(number);
            await new SalesQuotationDraftStore(create, new FixedClock(new DateTime(2026, 10, 29)))
                .CreateAsync(quote, Ct);
            await tx.CommitAsync(Ct);
            id = quote.Id;
        }

        var input = new SalesQuotationInput(22, new DateOnly(2028, 2, 29), new DateOnly(2028, 4, 1),
            " revised ", [new SalesQuotationLineInput(1, 32, 2m, 15m, 0m, "TWD", 42)]);
        await using var db = Context(1);
        SalesQuotationDraftCommandService Command(long userId, DataScope? scope) =>
            new(db, Access(1, userId, "SAL.QUOTATION.EDIT", scope),
                new SalesCompanyAccess(new FixedCompany(1)), Snapshots(),
                new SalesQuotationDraftStore(db, new FixedClock(new DateTime(2026, 10, 29))));

        await Assert.ThrowsAsync<ForbiddenException>(() => Command(7, null).ReplaceAsync(id, 1, input, Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => Command(8, DataScope.Self).ReplaceAsync(id, 1, input, Ct));
        await Assert.ThrowsAsync<ConflictException>(() => Command(7, DataScope.Self).ReplaceAsync(id, 2, input, Ct));
        var edited = await Command(7, DataScope.Self).ReplaceAsync(id, 1, input, Ct);
        Assert.Equal(2, edited.Version);
        Assert.Equal(number, edited.DocumentNo);
        Assert.Equal(7, edited.OwnerUserId);
        Assert.Equal("Revised customer", edited.CustomerName);
        Assert.Equal("Revised item", Assert.Single(edited.Lines).ItemName);
        Assert.Equal(32m, edited.OriginalGross);
        await using var verify = Context(1);
        var saved = await verify.Set<SalesQuotation>().Include(x => x.Lines).SingleAsync(x => x.Id == id, Ct);
        Assert.Equal("revised", saved.Remark);
        Assert.Equal(32m, saved.OriginalGross);
        Assert.Equal(32, Assert.Single(saved.Lines).ItemId);
        await Assert.ThrowsAsync<ConflictException>(() => Command(7, DataScope.Self).ReplaceAsync(id, 1, input, Ct));

        saved.Status = SalesQuotationStatus.Rejected;
        await verify.SaveChangesAsync(Ct);
        Assert.Equal(3, saved.Version);
        var revised = await Command(7, DataScope.Self).ReplaceAsync(id, 3, input, Ct);
        Assert.Equal(SalesQuotationStatus.Draft, revised.Status);
        Assert.Equal(4, revised.Version);
    }

    [Fact]
    public async Task DeleteRequiresItsOwnScopeAndCurrentVersion()
    {
        var number = "QD" + Guid.NewGuid().ToString("N")[..12];
        long id;
        await using (var create = Context(1))
        {
            await using var tx = await create.Database.BeginTransactionAsync(IsolationLevel.Serializable, Ct);
            var quote = Draft(number);
            await new SalesQuotationDraftStore(create, new FixedClock(new DateTime(2026, 10, 29)))
                .CreateAsync(quote, Ct);
            await tx.CommitAsync(Ct);
            id = quote.Id;
        }

        await using var db = Context(1);
        SalesQuotationDraftCommandService Command(long userId, DataScope? scope) =>
            new(db, Access(1, userId, "SAL.QUOTATION.DELETE", scope),
                new SalesCompanyAccess(new FixedCompany(1)), Snapshots(),
                new SalesQuotationDraftStore(db, new FixedClock(new DateTime(2026, 10, 29))));

        await Assert.ThrowsAsync<ForbiddenException>(() => Command(7, null).DeleteAsync(id, 1, Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => Command(8, DataScope.Self).DeleteAsync(id, 1, Ct));
        await Assert.ThrowsAsync<ConflictException>(() => Command(7, DataScope.Self).DeleteAsync(id, 2, Ct));
        await Command(7, DataScope.Self).DeleteAsync(id, 1, Ct);
        await Assert.ThrowsAsync<NotFoundException>(() => Command(7, DataScope.Self).DeleteAsync(id, 1, Ct));
        await using var verify = Context(1);
        Assert.False(await verify.Set<SalesQuotation>().AnyAsync(x => x.Id == id, Ct));
        Assert.Equal(number, (await verify.Set<SalesQuotation>().IgnoreQueryFilters()
            .SingleAsync(x => x.Id == id, Ct)).DocumentNo);
    }

    [Fact]
    public async Task ConcurrentEditsOfSameVersionHaveOneWinnerAndOneConflict()
    {
        var number = "QC" + Guid.NewGuid().ToString("N")[..12];
        long id;
        await using (var create = Context(1))
        {
            await using var tx = await create.Database.BeginTransactionAsync(IsolationLevel.Serializable, Ct);
            var quote = Draft(number);
            await new SalesQuotationDraftStore(create, new FixedClock(new DateTime(2026, 10, 29)))
                .CreateAsync(quote, Ct);
            await tx.CommitAsync(Ct);
            id = quote.Id;
        }

        var input = new SalesQuotationInput(22, new DateOnly(2028, 2, 29), new DateOnly(2028, 4, 1),
            "race", [new SalesQuotationLineInput(1, 32, 2m, 15m, 0m, "TWD", 42)]);
        async Task<string> EditAsync()
        {
            await using var db = Context(1);
            var command = new SalesQuotationDraftCommandService(db,
                Access(1, 7, "SAL.QUOTATION.EDIT", DataScope.Self),
                new SalesCompanyAccess(new FixedCompany(1)), Snapshots(),
                new SalesQuotationDraftStore(db, new FixedClock(new DateTime(2026, 10, 29))));
            try
            {
                await command.ReplaceAsync(id, 1, input, Ct);
                return "updated";
            }
            catch (ConflictException)
            {
                return "conflict";
            }
        }

        var results = await Task.WhenAll(EditAsync(), EditAsync());
        Assert.Equal(["conflict", "updated"], results.OrderBy(x => x).ToArray());
        await using var verify = Context(1);
        var saved = await verify.Set<SalesQuotation>().Include(x => x.Lines).SingleAsync(x => x.Id == id, Ct);
        Assert.Equal(2, saved.Version);
        Assert.Equal(number, saved.DocumentNo);
        Assert.Equal("race", saved.Remark);
        Assert.Single(saved.Lines);
    }

    [Fact]
    public async Task ConcurrentDeletesHaveOneWinnerAndOneNotFound()
    {
        var number = "QX" + Guid.NewGuid().ToString("N")[..12];
        long id;
        await using (var create = Context(1))
        {
            await using var tx = await create.Database.BeginTransactionAsync(IsolationLevel.Serializable, Ct);
            var quote = Draft(number);
            await new SalesQuotationDraftStore(create, new FixedClock(new DateTime(2026, 10, 29)))
                .CreateAsync(quote, Ct);
            await tx.CommitAsync(Ct);
            id = quote.Id;
        }

        async Task<string> DeleteAsync()
        {
            await using var db = Context(1);
            var command = new SalesQuotationDraftCommandService(db,
                Access(1, 7, "SAL.QUOTATION.DELETE", DataScope.Self),
                new SalesCompanyAccess(new FixedCompany(1)), Snapshots(),
                new SalesQuotationDraftStore(db, new FixedClock(new DateTime(2026, 10, 29))));
            try
            {
                await command.DeleteAsync(id, 1, Ct);
                return "deleted";
            }
            catch (NotFoundException)
            {
                return "not-found";
            }
        }

        var results = await Task.WhenAll(DeleteAsync(), DeleteAsync());
        Assert.Equal(["deleted", "not-found"], results.OrderBy(x => x).ToArray());
        await using var verify = Context(1);
        var saved = await verify.Set<SalesQuotation>().IgnoreQueryFilters().SingleAsync(x => x.Id == id, Ct);
        Assert.True(saved.IsDeleted);
        Assert.Equal(number, saved.DocumentNo);
    }

    [Fact]
    public async Task ListAppliesViewScopeBeforeCountFiltersAndStablePaging()
    {
        var prefix = "QL" + Guid.NewGuid().ToString("N")[..10];
        long ownId;
        await using (var create = Context(1))
        {
            await using var tx = await create.Database.BeginTransactionAsync(IsolationLevel.Serializable, Ct);
            var own = Draft(prefix + "01");
            var other = Draft(prefix + "02");
            other.OwnerUserId = 8;
            var store = new SalesQuotationDraftStore(create, new FixedClock(new DateTime(2026, 10, 29)));
            await store.CreateAsync(own, Ct);
            await store.CreateAsync(other, Ct);
            await tx.CommitAsync(Ct);
            ownId = own.Id;
        }

        await using var db = Context(1);
        SalesQuotationQueryService Query(DataScope? scope) => new(db,
            Access(1, 7, "SAL.QUOTATION.VIEW", scope),
            new SalesCompanyAccess(new FixedCompany(1)));
        var mine = await Query(DataScope.Self).ListAsync(new SalesQuotationQuery
        {
            Keyword = prefix, PageSize = 1
        }, Ct);
        Assert.Equal(1, mine.TotalCount);
        Assert.Equal(ownId, Assert.Single(mine.Items).Id);
        var company = await Query(DataScope.Company).ListAsync(new SalesQuotationQuery
        {
            Keyword = prefix, PageSize = 1
        }, Ct);
        Assert.Equal(2, company.TotalCount);
        Assert.Single(company.Items);
        var second = await Query(DataScope.Company).ListAsync(new SalesQuotationQuery
        {
            Keyword = prefix, Page = 2, PageSize = 1
        }, Ct);
        Assert.Equal(2, second.TotalCount);
        Assert.NotEqual(company.Items[0].Id, second.Items[0].Id);
        Assert.Equal(2, (await Query(DataScope.Company).ListAsync(new SalesQuotationQuery
        {
            Keyword = "  " + prefix + "  ", From = new DateOnly(2028, 2, 29),
            To = new DateOnly(2028, 2, 29), CustomerId = 21,
            Status = SalesQuotationStatus.Draft
        }, Ct)).TotalCount);
        Assert.Empty((await Query(DataScope.Company).ListAsync(new SalesQuotationQuery
        {
            Keyword = prefix, From = new DateOnly(2028, 3, 1)
        }, Ct)).Items);
        await Assert.ThrowsAsync<ForbiddenException>(() => Query(null).ListAsync(new SalesQuotationQuery(), Ct));
        await Assert.ThrowsAsync<MyERP.Shared.Errors.RequestValidationException>(() =>
            Query(DataScope.Company).ListAsync(new SalesQuotationQuery { Sort = "customerName" }, Ct));
        foreach (var invalid in new[]
        {
            new SalesQuotationQuery { Page = 0 },
            new SalesQuotationQuery { PageSize = 201 },
            new SalesQuotationQuery { From = new DateOnly(2028, 3, 1), To = new DateOnly(2028, 2, 29) },
            new SalesQuotationQuery { CustomerId = 0 },
            new SalesQuotationQuery { Status = (SalesQuotationStatus)99 }
        })
            await Assert.ThrowsAsync<MyERP.Shared.Errors.RequestValidationException>(() =>
                Query(DataScope.Company).ListAsync(invalid, Ct));
    }

    private MyErpDbContext Context(long companyId) => new(
        new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(new AuditStamper(
                new FixedClock(new DateTime(2026, 10, 29)), new FixedUser(7), new FixedCompany(companyId)))).Options,
        [new SalesQuotationDbModel()], new FixedCompany(companyId));

    private static SalesActionAccess Access(long companyId, long userId, string permission, DataScope? scope)
    {
        var auth = new Mock<ICurrentAuthorization>();
        auth.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            new UserCompanyAuthorization(userId, companyId, null, null, false,
                scope is null ? new Dictionary<string, DataScope>() :
                    new Dictionary<string, DataScope> { [permission] = scope.Value }));
        return new SalesActionAccess(new SalesCompanyAccess(new FixedCompany(companyId)), auth.Object);
    }

    private static SalesQuotation Draft(string number)
    {
        var quote = new SalesQuotation
        {
            CompanyId = 1, OwnerUserId = 7, DocumentNo = number, CustomerId = 21,
            CustomerCode = "C21", CustomerName = "Customer", BusinessDate = new DateOnly(2028, 2, 29),
            ExpiryDate = new DateOnly(2028, 3, 30), CurrencyCode = "TWD", BaseCurrencyCode = "TWD",
            ExchangeRate = 1, OriginalNet = 10, OriginalGross = 10, BaseNet = 10, BaseGross = 10
        };
        quote.Lines.Add(new SalesQuotationLine
        {
            CompanyId = 1, LineNo = 1, ItemId = 31, ItemCode = "I31", ItemName = "Item",
            TaxRateId = 41, Quantity = 1, UnitPrice = 10,
            OriginalNet = 10, OriginalGross = 10, BaseNet = 10, BaseGross = 10
        });
        return quote;
    }

    private static SalesQuotationSnapshotBuilder Snapshots()
    {
        var customer = new Mock<ICustomerLookup>();
        customer.Setup(x => x.RequireApprovedAsync(22, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CustomerIdentity(22, "C22", "Revised customer"));
        var item = new Mock<ISalesItemLookup>();
        item.Setup(x => x.RequireActiveAsync(32, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesItemIdentity(32, "I32", "Revised item"));
        var currency = new Mock<ICurrencyLookup>();
        currency.Setup(x => x.FindCurrencyAsync("TWD", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CurrencyInfo("TWD", "Dollar", null, 0, true, true));
        currency.Setup(x => x.GetBaseCurrencyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CurrencyInfo("TWD", "Dollar", null, 0, true, true));
        currency.Setup(x => x.GetRateSnapshotAsync("TWD", new DateOnly(2028, 2, 29),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExchangeRateSnapshot(1m, new DateOnly(2028, 2, 29), null, null));
        currency.Setup(x => x.GetPrecisionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CurrencyPrecision.Default);
        var tax = new Mock<ITaxRateLookup>();
        tax.Setup(x => x.GetRateSnapshotAsync(42, TaxDirection.Output,
                new DateOnly(2028, 2, 29), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaxRateSnapshot(42, 5m, new DateOnly(2028, 2, 29), null));
        return new SalesQuotationSnapshotBuilder(customer.Object, item.Object, currency.Object, tax.Object);
    }
}
