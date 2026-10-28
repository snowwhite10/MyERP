using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Sales.Application;
using MyERP.Modules.Sales.Domain;
using MyERP.Modules.Sales.Infrastructure;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.IntegrationTests.Database;

public sealed class SalesOrderSchemaTests(TestDatabase database)
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ConcurrentDraftEditAndDeleteSerializeAndPreserveCommittedState(
        bool deleteFirst, bool commitWinner)
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = TestCompanies.Next();
        long id;
        string number;
        await using (var setup = Context(companyId))
        {
            await using var transaction = await setup.Database.BeginTransactionAsync(token);
            var draft = NewOrder(companyId, $"RACE{companyId}", null);
            draft.Lines.Add(NewLine(companyId, null, 1m));
            await new SalesOrderDraftStore(setup, new FixedClock(new DateTime(2026, 10, 27)))
                .CreateAsync(draft, token);
            id = draft.Id;
            number = draft.DocumentNo;
            await transaction.CommitAsync(token);
        }

        await using (var winner = Context(companyId))
        await using (var winnerTransaction = await winner.Database.BeginTransactionAsync(token))
        {
            var winnerStore = new SalesOrderDraftStore(winner, new FixedClock(new DateTime(2026, 10, 27)));
            if (deleteFirst)
                await winnerStore.SoftDeleteAsync(id, 1, token);
            else
                await winnerStore.ReplaceAsync(id, 1, Replacement(companyId), token);

            await using (var contender = Context(companyId))
            {
                await contender.Database.OpenConnectionAsync(token);
                await contender.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT 1000", token);
                await using var contenderTransaction = await contender.Database.BeginTransactionAsync(token);
                var contenderStore = new SalesOrderDraftStore(contender, new FixedClock(new DateTime(2026, 10, 27)));
                var blocked = await Assert.ThrowsAsync<SqlException>(() => deleteFirst
                    ? contenderStore.ReplaceAsync(id, 1, Replacement(companyId), token)
                    : contenderStore.SoftDeleteAsync(id, 1, token));
                Assert.Equal(1222, blocked.Number);
                await contenderTransaction.RollbackAsync(token);
            }

            if (commitWinner)
                await winnerTransaction.CommitAsync(token);
            else
                await winnerTransaction.RollbackAsync(token);
        }

        await using (var verifyRollback = Context(companyId))
        {
            var original = await verifyRollback.Set<SalesOrder>().IgnoreQueryFilters().AsNoTracking()
                .Include(x => x.Lines)
                .SingleAsync(x => x.Id == id, token);
            Assert.Equal(number, original.DocumentNo);
            Assert.Equal(commitWinner ? 2 : 1, original.Version);
            Assert.Equal(commitWinner && !deleteFirst ? 2m : 1m,
                Assert.Single(original.Lines).Quantity);
            Assert.Equal(commitWinner && deleteFirst, original.IsDeleted);
        }

        await using (var retry = Context(companyId))
        await using (var retryTransaction = await retry.Database.BeginTransactionAsync(token))
        {
            var retryStore = new SalesOrderDraftStore(retry, new FixedClock(new DateTime(2026, 10, 27)));
            if (commitWinner)
            {
                if (deleteFirst)
                    await Assert.ThrowsAsync<MyERP.Shared.Errors.NotFoundException>(() =>
                        retryStore.ReplaceAsync(id, 1, Replacement(companyId), token));
                else
                    await Assert.ThrowsAsync<MyERP.Shared.Errors.ConflictException>(() =>
                        retryStore.SoftDeleteAsync(id, 1, token));
                await retryTransaction.RollbackAsync(token);
            }
            else
            {
                if (deleteFirst)
                    await retryStore.ReplaceAsync(id, 1, Replacement(companyId), token);
                else
                    await retryStore.SoftDeleteAsync(id, 1, token);
                await retryTransaction.CommitAsync(token);
            }
        }

        await using var final = Context(companyId);
        var saved = await final.Set<SalesOrder>().IgnoreQueryFilters().AsNoTracking().Include(x => x.Lines)
            .SingleAsync(x => x.Id == id, token);
        Assert.Equal(number, saved.DocumentNo);
        Assert.Equal(2, saved.Version);
        Assert.Equal(commitWinner ? deleteFirst : !deleteFirst, saved.IsDeleted);
        Assert.Equal(commitWinner == deleteFirst ? 1m : 2m,
            Assert.Single(saved.Lines).Quantity);
    }

    private static SalesOrder Replacement(long companyId)
    {
        var replacement = NewOrder(companyId, "MUST-NOT-REPLACE-NUMBER", null);
        replacement.Lines.Add(NewLine(companyId, null, 2m));
        return replacement;
    }

    [Fact]
    public async Task ManualDraftStoreCreatesReplacesAndSoftDeletesAtomically()
    {
        var companyId = TestCompanies.Next();
        long id;
        string number;
        await using (var db = Context(companyId))
        {
            await using var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            var store = new SalesOrderDraftStore(db, new FixedClock(new DateTime(2026, 10, 27)));
            var draft = NewOrder(companyId, $"M{companyId}", null);
            draft.Lines.Add(NewLine(companyId, null, 1m));
            await store.CreateAsync(draft, TestContext.Current.CancellationToken);
            id = draft.Id;
            number = draft.DocumentNo;
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = Context(companyId))
        {
            await using var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            var store = new SalesOrderDraftStore(db, new FixedClock(new DateTime(2026, 10, 27)));
            var replacement = NewOrder(companyId, "CLIENT-CANNOT-CHANGE-NUMBER", null);
            replacement.Remark = "Revised";
            replacement.Lines.Add(NewLine(companyId, null, 2m));
            replacement.Lines.Add(NewLine(companyId, null, 3m));
            replacement.Lines[1].LineNo = 2;
            await store.ReplaceAsync(id, 1, replacement, TestContext.Current.CancellationToken);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = Context(companyId))
        {
            var saved = await db.Set<SalesOrder>().Include(x => x.Lines)
                .SingleAsync(x => x.Id == id, TestContext.Current.CancellationToken);
            Assert.Equal(number, saved.DocumentNo);
            Assert.Equal("Revised", saved.Remark);
            Assert.Equal(2, saved.Version);
            Assert.Equal([2m, 3m], saved.Lines.OrderBy(x => x.LineNo).Select(x => x.Quantity));
            await using var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            var store = new SalesOrderDraftStore(db, new FixedClock(new DateTime(2026, 10, 27)));
            await Assert.ThrowsAsync<MyERP.Shared.Errors.ConflictException>(() =>
                store.SoftDeleteAsync(id, 1, TestContext.Current.CancellationToken));
            await store.SoftDeleteAsync(id, 2, TestContext.Current.CancellationToken);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }

        await using var verify = Context(companyId);
        Assert.False(await verify.Set<SalesOrder>().AnyAsync(x => x.Id == id,
            TestContext.Current.CancellationToken));
        var deleted = await verify.Set<SalesOrder>().IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x => x.Id == id, TestContext.Current.CancellationToken);
        Assert.True(deleted.IsDeleted);
        Assert.Equal(3, deleted.Version);
        Assert.Equal(number, deleted.DocumentNo);
        Assert.Equal(2, await verify.Set<SalesOrderLine>().CountAsync(x => x.OrderId == id,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ManualDraftStoreRequiresTransactionAndRollbackRemovesDraft()
    {
        var companyId = TestCompanies.Next();
        var number = $"RB{companyId}";
        await using (var db = Context(companyId))
        {
            var store = new SalesOrderDraftStore(db, new FixedClock(new DateTime(2026, 10, 27)));
            var draft = NewOrder(companyId, number, null);
            draft.Lines.Add(NewLine(companyId, null, 1m));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.CreateAsync(draft, TestContext.Current.CancellationToken));
            await using var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            await store.CreateAsync(draft, TestContext.Current.CancellationToken);
            await transaction.RollbackAsync(TestContext.Current.CancellationToken);
        }

        await using var verify = Context(companyId);
        Assert.False(await verify.Set<SalesOrder>().IgnoreQueryFilters().AnyAsync(x => x.DocumentNo == number,
            TestContext.Current.CancellationToken));
        Assert.Equal(0, await verify.Set<SalesOrderLine>().CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ManualDraftStoreHidesOtherCompanyAndRejectsApprovedEdit()
    {
        var companyId = TestCompanies.Next();
        var otherCompanyId = TestCompanies.Next();
        long id;
        await using (var setup = Context(companyId))
        {
            var draft = NewOrder(companyId, $"AC{companyId}", null);
            draft.Lines.Add(NewLine(companyId, null, 1m));
            setup.Add(draft);
            await setup.SaveChangesAsync(TestContext.Current.CancellationToken);
            id = draft.Id;
        }

        await using (var other = Context(otherCompanyId))
        {
            await using var transaction = await other.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            var store = new SalesOrderDraftStore(other, new FixedClock(new DateTime(2026, 10, 27)));
            var foreignDraft = NewOrder(companyId, $"FOREIGN{companyId}", null);
            foreignDraft.Lines.Add(NewLine(companyId, null, 1m));
            await Assert.ThrowsAsync<MyERP.Shared.Errors.ForbiddenException>(() =>
                store.CreateAsync(foreignDraft, TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<MyERP.Shared.Errors.NotFoundException>(() =>
                store.SoftDeleteAsync(id, 1, TestContext.Current.CancellationToken));
        }

        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using (var approve = connection.CreateCommand())
        {
            approve.CommandText = "UPDATE sal_order SET status = 'Approved' WHERE id = @id";
            approve.Parameters.AddWithValue("@id", id);
            Assert.Equal(1, await approve.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        }
        await using (var db = Context(companyId))
        {
            await using var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            var store = new SalesOrderDraftStore(db, new FixedClock(new DateTime(2026, 10, 27)));
            var replacement = NewOrder(companyId, "IGNORED", null);
            replacement.Lines.Add(NewLine(companyId, null, 2m));
            await Assert.ThrowsAsync<MyERP.Shared.Errors.BusinessRuleException>(() =>
                store.ReplaceAsync(id, 1, replacement, TestContext.Current.CancellationToken));
        }

        await using var verify = Context(companyId);
        var saved = await verify.Set<SalesOrder>().AsNoTracking().SingleAsync(x => x.Id == id,
            TestContext.Current.CancellationToken);
        Assert.Equal(SalesOrderStatus.Approved, saved.Status);
        Assert.Equal(1, saved.Version);
        Assert.False(saved.IsDeleted);
    }

    [Fact]
    public async Task PhysicalDeleteCannotReleaseOrderNumberOrQuotationSource()
    {
        foreach (var (status, softDeleted) in new[]
        {
            (SalesOrderStatus.Draft, false),
            (SalesOrderStatus.Draft, true),
            (SalesOrderStatus.Approved, false)
        })
        {
            var companyId = TestCompanies.Next();
            long orderId;
            long lineId;
            await using (var setup = Context(companyId))
            {
                var order = NewOrder(companyId, $"PH{companyId}", 810);
                order.Lines.Add(NewLine(companyId, 811, 1m));
                setup.Add(order);
                await setup.SaveChangesAsync(TestContext.Current.CancellationToken);
                orderId = order.Id;
                lineId = order.Lines[0].Id;
            }

            await using var connection = new SqlConnection(database.ConnectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            if (status == SalesOrderStatus.Approved)
            {
                await using var approve = connection.CreateCommand();
                approve.CommandText = "UPDATE sal_order SET status = 'Approved' WHERE id = @id";
                approve.Parameters.AddWithValue("@id", orderId);
                Assert.Equal(1, await approve.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
            }
            if (softDeleted)
            {
                await using var softDelete = connection.CreateCommand();
                softDelete.CommandText = "UPDATE sal_order SET is_deleted = 1, deleted_at = '2026-10-27T00:00:00' WHERE id = @id";
                softDelete.Parameters.AddWithValue("@id", orderId);
                Assert.Equal(1, await softDelete.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
            }

            await using (var remove = connection.CreateCommand())
            {
                remove.CommandText = "DELETE FROM sal_order WHERE id = @id";
                remove.Parameters.AddWithValue("@id", orderId);
                await Assert.ThrowsAsync<SqlException>(() =>
                    remove.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
            }

            await using var verify = Context(companyId);
            var saved = await verify.Set<SalesOrder>().IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(x => x.Id == orderId, TestContext.Current.CancellationToken);
            Assert.Equal(status, saved.Status);
            Assert.Equal(softDeleted, saved.IsDeleted);
            Assert.Equal(810, saved.SourceQuotationId);
            Assert.Equal($"PH{companyId}", saved.DocumentNo);
            Assert.True(await verify.Set<SalesOrderLine>().AnyAsync(x => x.Id == lineId,
                TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task ApprovedOrderCommercialHeaderCannotBeChangedButDraftCanBeEdited()
    {
        var companyId = TestCompanies.Next();
        long approvedId;
        long draftId;
        await using (var setup = Context(companyId))
        {
            var approved = NewOrder(companyId, $"OH{companyId}", null);
            approved.Lines.Add(NewLine(companyId, null, 1m));
            var draft = NewOrder(companyId, $"DH{companyId}", null);
            draft.Lines.Add(NewLine(companyId, null, 1m));
            setup.AddRange(approved, draft);
            await setup.SaveChangesAsync(TestContext.Current.CancellationToken);
            approvedId = approved.Id;
            draftId = draft.Id;
        }

        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using (var approve = connection.CreateCommand())
        {
            approve.CommandText = "UPDATE sal_order SET status = 'Approved' WHERE id = @id";
            approve.Parameters.AddWithValue("@id", approvedId);
            Assert.Equal(1, await approve.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        }

        foreach (var sql in new[]
        {
            "UPDATE sal_order SET customer_name = 'Changed' WHERE id = @id",
            "UPDATE sal_order SET original_gross = 50 WHERE id = @id",
            "UPDATE sal_order SET remark = 'Changed' WHERE id = @id"
        })
        {
            await using var change = connection.CreateCommand();
            change.CommandText = sql;
            change.Parameters.AddWithValue("@id", approvedId);
            await Assert.ThrowsAsync<SqlException>(() =>
                change.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        }

        await using (var editDraft = connection.CreateCommand())
        {
            editDraft.CommandText = "UPDATE sal_order SET remark = 'Draft edit' WHERE id = @id";
            editDraft.Parameters.AddWithValue("@id", draftId);
            Assert.Equal(1, await editDraft.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        }

        await using var verify = Context(companyId);
        var saved = await verify.Set<SalesOrder>().AsNoTracking()
            .SingleAsync(x => x.Id == approvedId, TestContext.Current.CancellationToken);
        Assert.Equal("Customer", saved.CustomerName);
        Assert.Equal(0m, saved.OriginalGross);
        Assert.Null(saved.Remark);
    }

    [Fact]
    public async Task SavedOrderCannotReturnFromApprovedToDraftOrReviveDeletedDraft()
    {
        var companyId = TestCompanies.Next();
        long approvedId;
        long deletedId;
        await using (var setup = Context(companyId))
        {
            var approved = NewOrder(companyId, $"OA{companyId}", null);
            approved.Lines.Add(NewLine(companyId, null, 1m));
            var deleted = NewOrder(companyId, $"OD{companyId}", null);
            deleted.Lines.Add(NewLine(companyId, null, 1m));
            setup.AddRange(approved, deleted);
            await setup.SaveChangesAsync(TestContext.Current.CancellationToken);
            approvedId = approved.Id;
            deletedId = deleted.Id;
        }

        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using (var approve = connection.CreateCommand())
        {
            approve.CommandText = "UPDATE sal_order SET status = 'Approved' WHERE id = @id";
            approve.Parameters.AddWithValue("@id", approvedId);
            Assert.Equal(1, await approve.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        }
        await using (var delete = connection.CreateCommand())
        {
            delete.CommandText = "UPDATE sal_order SET is_deleted = 1, deleted_at = '2026-10-27T00:00:00' WHERE id = @id";
            delete.Parameters.AddWithValue("@id", deletedId);
            Assert.Equal(1, await delete.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        }

        foreach (var (sql, id) in new[]
        {
            ("UPDATE sal_order SET status = 'Draft' WHERE id = @id", approvedId),
            ("UPDATE sal_order SET is_deleted = 0, deleted_at = NULL WHERE id = @id", deletedId)
        })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("@id", id);
            await Assert.ThrowsAsync<SqlException>(() =>
                command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        }

        await using var verify = connection.CreateCommand();
        verify.CommandText = "SELECT status FROM sal_order WHERE id = @approved_id; SELECT is_deleted FROM sal_order WHERE id = @deleted_id";
        verify.Parameters.AddWithValue("@approved_id", approvedId);
        verify.Parameters.AddWithValue("@deleted_id", deletedId);
        await using var reader = await verify.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Approved", reader.GetString(0));
        Assert.True(await reader.NextResultAsync(TestContext.Current.CancellationToken));
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.True(reader.GetBoolean(0));
    }

    private MyErpDbContext Context(long companyId)
    {
        var company = new FixedCompany(companyId);
        return new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
                .UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(new AuditStamper(
                    new FixedClock(new DateTime(2026, 10, 27)), new FixedUser(7), company))).Options,
            [new SalesOrderDbModel()], company);
    }

    [Fact]
    public async Task ApprovedOrDeletedOrderLinesCannotBeChanged()
    {
        foreach (var deleted in new[] { false, true })
        {
            var companyId = TestCompanies.Next();
            long orderId;
            long lineId;
            await using (var setup = Context(companyId))
            {
                var order = NewOrder(companyId, $"O{companyId}", null);
                order.Lines.Add(NewLine(companyId, null, 1m));
                setup.Add(order);
                await setup.SaveChangesAsync(TestContext.Current.CancellationToken);
                orderId = order.Id;
                lineId = order.Lines[0].Id;
            }

            await using var connection = new SqlConnection(database.ConnectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using (var freeze = connection.CreateCommand())
            {
                freeze.CommandText = deleted
                    ? "UPDATE sal_order SET is_deleted = 1, deleted_at = '2026-10-27T00:00:00' WHERE id = @id"
                    : "UPDATE sal_order SET status = 'Approved' WHERE id = @id";
                freeze.Parameters.AddWithValue("@id", orderId);
                Assert.Equal(1, await freeze.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
            }

            foreach (var sql in new[]
            {
                "UPDATE sal_order_line SET quantity = 2 WHERE id = @line_id",
                "DELETE FROM sal_order_line WHERE id = @line_id",
                "INSERT INTO sal_order_line (company_id, order_id, line_no, item_id, item_code, item_name, quantity, unit_price, discount_rate, tax_rate_id, tax_rate_percent, original_net, original_tax, original_gross, base_net, base_tax, base_gross, created_at, created_by, version) SELECT company_id, order_id, 2, item_id, item_code, item_name, quantity, unit_price, discount_rate, tax_rate_id, tax_rate_percent, original_net, original_tax, original_gross, base_net, base_tax, base_gross, created_at, created_by, 1 FROM sal_order_line WHERE id = @line_id"
            })
            {
                await using var command = connection.CreateCommand();
                command.CommandText = sql;
                command.Parameters.AddWithValue("@line_id", lineId);
                await Assert.ThrowsAsync<SqlException>(() =>
                    command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
            }

            await using var verify = Context(companyId);
            Assert.Equal(1m, (await verify.Set<SalesOrderLine>().SingleAsync(x => x.Id == lineId,
                TestContext.Current.CancellationToken)).Quantity);
            Assert.Equal(1, await verify.Set<SalesOrderLine>().CountAsync(TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task OrderTablesEnforceCompanySourceAndSavedQuantity()
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sys.tables WHERE name IN ('sal_order', 'sal_order_line')";
        Assert.Equal(2, (int)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!);

        command.CommandText = "SELECT COUNT(*) FROM sys.check_constraints WHERE name IN ('ck_sal_order_status', 'ck_sal_order_line_quantity')";
        Assert.Equal(2, (int)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!);

        command.CommandText = "SELECT COUNT(*) FROM sys.indexes WHERE name IN ('ix_sal_order_company_id_source_quotation_id', 'ix_sal_order_line_company_id_source_quotation_line_id') AND is_unique = 1";
        Assert.Equal(2, (int)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!);

        command.CommandText = "SELECT COUNT(*) FROM sys.foreign_keys WHERE name = 'fk_sal_order_line_sal_order_company_id_order_id'";
        Assert.Equal(1, (int)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!);
    }

    [Fact]
    public async Task SourceLinkIsPermanentAndInvalidQuantityRollsBackWholeOrder()
    {
        var companyId = TestCompanies.Next();
        await using (var db = Context(companyId))
        {
            var order = NewOrder(companyId, $"O{companyId}", 901);
            order.Lines.Add(NewLine(companyId, 902, 1m));
            db.Add(order);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            Assert.Equal(SalesOrderStatus.Draft, order.Status);
            Assert.Single(order.Lines);
        }

        await using (var duplicate = Context(companyId))
        {
            var order = NewOrder(companyId, $"O{companyId}B", 901);
            order.Lines.Add(NewLine(companyId, 903, 1m));
            duplicate.Add(order);
            await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync(TestContext.Current.CancellationToken));
        }

        await using (var invalid = Context(companyId))
        {
            var order = NewOrder(companyId, $"O{companyId}C", null);
            order.Lines.Add(NewLine(companyId, null, 0m));
            invalid.Add(order);
            await Assert.ThrowsAsync<DbUpdateException>(() => invalid.SaveChangesAsync(TestContext.Current.CancellationToken));
        }

        await using var verify = Context(companyId);
        Assert.Equal(1, await verify.Set<SalesOrder>().CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await verify.Set<SalesOrderLine>().CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OrderAndLineQuotationSourcesMustAgree()
    {
        foreach (var (orderSource, lineSource) in new (long?, long?)[]
        {
            (null, 801), (802, null)
        })
        {
            var companyId = TestCompanies.Next();
            await using var db = Context(companyId);
            var order = NewOrder(companyId, $"O{companyId}", orderSource);
            order.Lines.Add(NewLine(companyId, lineSource, 1m));
            db.Add(order);
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                db.SaveChangesAsync(TestContext.Current.CancellationToken));

            await using var verify = Context(companyId);
            Assert.Equal(0, await verify.Set<SalesOrder>().CountAsync(TestContext.Current.CancellationToken));
            Assert.Equal(0, await verify.Set<SalesOrderLine>().CountAsync(TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task SavedQuotationSourceCannotBeReassignedOrCleared()
    {
        var companyId = TestCompanies.Next();
        long orderId;
        long lineId;
        await using (var setup = Context(companyId))
        {
            var order = NewOrder(companyId, $"O{companyId}", 910);
            order.Lines.Add(NewLine(companyId, 911, 1m));
            setup.Add(order);
            await setup.SaveChangesAsync(TestContext.Current.CancellationToken);
            orderId = order.Id;
            lineId = order.Lines[0].Id;
        }

        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        foreach (var sql in new[]
        {
            "UPDATE sal_order SET source_quotation_id = NULL WHERE id = @id",
            "UPDATE sal_order SET source_quotation_id = 912 WHERE id = @id",
            "UPDATE sal_order_line SET source_quotation_line_id = NULL WHERE id = @id",
            "UPDATE sal_order_line SET source_quotation_line_id = 913 WHERE id = @id"
        })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("@id", sql.Contains("sal_order_line", StringComparison.Ordinal) ? lineId : orderId);
            await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        }

        await using var verify = Context(companyId);
        Assert.Equal(910, (await verify.Set<SalesOrder>().SingleAsync(x => x.Id == orderId,
            TestContext.Current.CancellationToken)).SourceQuotationId);
        Assert.Equal(911, (await verify.Set<SalesOrderLine>().SingleAsync(x => x.Id == lineId,
            TestContext.Current.CancellationToken)).SourceQuotationLineId);

        await using (var softDelete = connection.CreateCommand())
        {
            softDelete.CommandText = "UPDATE sal_order SET remark = 'checked', is_deleted = 1, deleted_at = '2026-10-27T00:00:00' WHERE id = @id";
            softDelete.Parameters.AddWithValue("@id", orderId);
            Assert.Equal(1, await softDelete.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        }
        await using (var changeDeletedSource = connection.CreateCommand())
        {
            changeDeletedSource.CommandText = "UPDATE sal_order SET source_quotation_id = NULL WHERE id = @id";
            changeDeletedSource.Parameters.AddWithValue("@id", orderId);
            await Assert.ThrowsAsync<SqlException>(() =>
                changeDeletedSource.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        }
    }

    private static SalesOrder NewOrder(long companyId, string number, long? quotationId) => new()
    {
        CompanyId = companyId, DocumentNo = number, CustomerId = 501,
        CustomerCode = "C501", CustomerName = "Customer", BusinessDate = new DateOnly(2026, 10, 27),
        CurrencyCode = "TWD", BaseCurrencyCode = "TWD", ExchangeRate = 1m,
        SourceQuotationId = quotationId
    };

    private static SalesOrderLine NewLine(long companyId, long? sourceId, decimal quantity) => new()
    {
        CompanyId = companyId, LineNo = 1, ItemId = 601, ItemCode = "I601", ItemName = "Item",
        SourceQuotationLineId = sourceId, Quantity = quantity, UnitPrice = 1m,
        TaxRateId = 701, TaxRatePercent = 0m
    };
}
