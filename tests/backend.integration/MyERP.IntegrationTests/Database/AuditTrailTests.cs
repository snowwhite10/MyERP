using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Shared.Auditing;
using MyERP.Shared.Infrastructure.Auditing;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.IntegrationTests.Database;

/// <summary>SYS-AUDIT-001～008：稽核 interceptor 對真實 SQL Server 生效——欄位差異、同一交易、回滾、敏感欄位、只增不改。</summary>
public sealed class AuditTrailTests(TestDatabase database)
{
    private static readonly DateTime Now = new(2026, 10, 11, 9, 15, 30);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private (MyErpDbContext Db, AuditTrail Trail) Create(long companyId, long? userId = 7)
    {
        var company = new FixedCompany(companyId);
        var user = new FixedUser(userId);
        var clock = new FixedClock(Now);
        var trail = new AuditTrail();
        var options = new DbContextOptionsBuilder<MyErpDbContext>()
            .UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(
                new AuditSaveChangesInterceptor(new AuditStamper(clock, user, company)),
                new AuditTrailInterceptor(trail, clock, user, company, new HttpContextAccessor()))
            .Options;
        return (new MyErpDbContext(options, [new TestModuleDbModel(), new AuditLogDbModel()], company), trail);
    }

    private async Task<List<AuditLogEntry>> EntriesAsync(string table, long id)
    {
        var options = new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString).Options;
        await using var db = new MyErpDbContext(options, [new AuditLogDbModel()], new FixedCompany(null));
        var key = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return await db.Set<AuditLogEntry>().AsNoTracking()
            .Where(e => e.TableName == table && e.RecordId == key)
            .OrderBy(e => e.Id)
            .ToListAsync(Ct);
    }

    private static JsonElement Changes(AuditLogEntry entry) => JsonDocument.Parse(entry.Changes!).RootElement;

    [Fact]
    public async Task Insert_WritesCreateEntryWithGeneratedIdAndNewValues()
    {
        var companyId = TestCompanies.Next();
        var (db, _) = Create(companyId);
        await using (db)
        {
            var order = new TestOrder { DocNo = "SO-A1", Amount = 12.5m };
            db.Add(order);
            await db.SaveChangesAsync(Ct);

            var entry = (await EntriesAsync("tst_order", order.Id)).Should().ContainSingle().Subject;
            entry.Action.Should().Be(AuditActions.Create);
            entry.Category.Should().Be(AuditCategories.Data);
            entry.CompanyId.Should().Be(companyId);
            entry.UserId.Should().Be(7);
            entry.OccurredAt.Should().Be(Now);
            var changes = Changes(entry);
            changes.GetProperty("doc_no").GetProperty("new").GetString().Should().Be("SO-A1");
            changes.GetProperty("amount").GetProperty("new").GetDecimal().Should().Be(12.5m);
            changes.GetProperty("doc_no").TryGetProperty("old", out _).Should().BeFalse();
            foreach (var excluded in new[] { "id", "version", "created_at", "created_by", "company_id" })
            {
                changes.TryGetProperty(excluded, out _).Should().BeFalse($"{excluded} is not a business change");
            }
        }
    }

    [Fact]
    public async Task Update_WritesOnlyChangedColumnsWithOldAndNewValues()
    {
        var companyId = TestCompanies.Next();
        long id;
        var (db, _) = Create(companyId);
        await using (db)
        {
            var order = new TestOrder { DocNo = "SO-U1", Amount = 10m };
            db.Add(order);
            await db.SaveChangesAsync(Ct);
            id = order.Id;
        }

        var (update, _) = Create(companyId, userId: 9);
        await using (update)
        {
            var order = await update.Set<TestOrder>().SingleAsync(o => o.Id == id, Ct);
            order.Amount = 25.75m;
            order.DocNo = "SO-U1";   // 同值不算變更
            await update.SaveChangesAsync(Ct);

            // 沒有任何欄位變更的 SaveChanges 不寫稽核
            await update.SaveChangesAsync(Ct);
        }

        var entries = await EntriesAsync("tst_order", id);
        entries.Select(e => e.Action).Should().Equal(AuditActions.Create, AuditActions.Update);
        var updated = entries[1];
        updated.UserId.Should().Be(9);
        var changes = Changes(updated);
        changes.EnumerateObject().Select(p => p.Name).Should().Equal("amount");
        changes.GetProperty("amount").GetProperty("old").GetDecimal().Should().Be(10m);
        changes.GetProperty("amount").GetProperty("new").GetDecimal().Should().Be(25.75m);
    }

    [Fact]
    public async Task SoftDeleteMarkedActionsAndSensitiveColumns()
    {
        var companyId = TestCompanies.Next();
        long id;
        var (db, trail) = Create(companyId);
        await using (db)
        {
            var customer = new TestCustomer { Code = "C1", Name = "Alpha", Secret = "top-secret" };
            db.Add(customer);
            await db.SaveChangesAsync(Ct);
            id = customer.Id;

            customer.Secret = "new-secret";
            trail.MarkAction(customer, AuditActions.Approve);
            await db.SaveChangesAsync(Ct);

            customer.IsDeleted = true;
            await db.SaveChangesAsync(Ct);
        }

        var entries = await EntriesAsync("tst_customer", id);
        entries.Select(e => e.Action).Should().Equal(AuditActions.Create, AuditActions.Approve, AuditActions.Delete);
        entries.Should().AllSatisfy(e => e.Changes.Should().NotContain("secret\":\"top").And.NotContain("new-secret"));
        Changes(entries[0]).GetProperty("secret").GetProperty("new").GetString().Should().Be("***");
        var approved = Changes(entries[1]).GetProperty("secret");
        approved.GetProperty("old").GetString().Should().Be("***");
        approved.GetProperty("new").GetString().Should().Be("***");
        Changes(entries[2]).GetProperty("is_deleted").GetProperty("new").GetBoolean().Should().BeTrue();

        trail.Invoking(t => t.MarkAction(new TestCustomer(), AuditActions.Delete)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task HardDelete_WritesOldValues()
    {
        var companyId = TestCompanies.Next();
        var (db, _) = Create(companyId);
        await using (db)
        {
            var order = new TestOrder { DocNo = "SO-D1", Amount = 3m };
            db.Add(order);
            await db.SaveChangesAsync(Ct);
            db.Remove(order);
            await db.SaveChangesAsync(Ct);

            var entries = await EntriesAsync("tst_order", order.Id);
            entries.Select(e => e.Action).Should().Equal(AuditActions.Create, AuditActions.Delete);
            var changes = Changes(entries[1]).GetProperty("doc_no");
            changes.GetProperty("old").GetString().Should().Be("SO-D1");
            changes.TryGetProperty("new", out _).Should().BeFalse();
        }
    }

    [Fact]
    public async Task RolledBackTransaction_LeavesNoAuditEntries()
    {
        var companyId = TestCompanies.Next();
        long insertedId;
        long updatedId;
        var (seed, _) = Create(companyId);
        await using (seed)
        {
            var order = new TestOrder { DocNo = "SO-R0", Amount = 1m };
            seed.Add(order);
            await seed.SaveChangesAsync(Ct);
            updatedId = order.Id;
        }

        var (db, _) = Create(companyId);
        await using (db)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(Ct);
            var inserted = new TestOrder { DocNo = "SO-R1", Amount = 2m };
            db.Add(inserted);
            (await db.Set<TestOrder>().SingleAsync(o => o.Id == updatedId, Ct)).Amount = 99m;
            await db.SaveChangesAsync(Ct);
            insertedId = inserted.Id;
            await transaction.RollbackAsync(Ct);
        }

        (await EntriesAsync("tst_order", insertedId)).Should().BeEmpty();
        (await EntriesAsync("tst_order", updatedId)).Select(e => e.Action).Should().Equal(AuditActions.Create);
    }

    [Fact]
    public async Task FailedSave_DoesNotWriteOrDuplicateAuditEntries()
    {
        var companyId = TestCompanies.Next();
        var (db, _) = Create(companyId);
        await using (db)
        {
            var order = new TestOrder { DocNo = "SO-F1", Amount = 1m };
            db.Add(order);
            await db.SaveChangesAsync(Ct);

            order.Amount = 2m;
            db.Entry(order).Property(o => o.Version).OriginalValue = 0; // 舊版本 → 409
            await db.Invoking(d => d.SaveChangesAsync(Ct)).Should().ThrowAsync<DbUpdateConcurrencyException>();

            // 改帶正確版本重試：只留一筆 UPDATE
            db.Entry(order).Property(o => o.Version).OriginalValue = 1;
            await db.SaveChangesAsync(Ct);

            (await EntriesAsync("tst_order", order.Id)).Select(e => e.Action).Should().Equal(AuditActions.Create, AuditActions.Update);
        }
    }

    [Fact]
    public async Task SecurityEventsAreSavedWithTheNextSaveChanges()
    {
        var companyId = TestCompanies.Next();
        var (db, trail) = Create(companyId);
        await using (db)
        {
            var username = "audit-" + Guid.NewGuid().ToString("N")[..12];
            trail.AddSecurityEvent(new SecurityAuditEvent(AuditActions.LoginFailed, null, username, companyId,
                new Dictionary<string, object?> { ["reason"] = "INVALID_CREDENTIALS", ["roleIds"] = new long[] { 3, 1 } }));
            await db.SaveChangesAsync(Ct);

            var saved = await db.Set<AuditLogEntry>().AsNoTracking().SingleAsync(e => e.Username == username, Ct);
            saved.Category.Should().Be(AuditCategories.Security);
            saved.Action.Should().Be(AuditActions.LoginFailed);
            saved.CompanyId.Should().Be(companyId);
            saved.TableName.Should().BeNull();
            var detail = Changes(saved);
            detail.GetProperty("reason").GetString().Should().Be("INVALID_CREDENTIALS");
            detail.GetProperty("roleIds").EnumerateArray().Select(x => x.GetInt64()).Should().Equal(3, 1);
        }
    }

    [Fact]
    public async Task AuditLogIsAppendOnly()
    {
        var companyId = TestCompanies.Next();
        var (db, _) = Create(companyId);
        await using (db)
        {
            var order = new TestOrder { DocNo = "SO-AO", Amount = 1m };
            db.Add(order);
            await db.SaveChangesAsync(Ct);
            var entryId = (await EntriesAsync("tst_order", order.Id)).Single().Id;

            await db.Invoking(d => d.Database.ExecuteSqlAsync($"UPDATE sys_audit_log SET action = 'TAMPERED' WHERE id = {entryId}", Ct))
                .Should().ThrowAsync<SqlException>().WithMessage("*append-only*");
            await db.Invoking(d => d.Database.ExecuteSqlAsync($"DELETE FROM sys_audit_log WHERE id = {entryId}", Ct))
                .Should().ThrowAsync<SqlException>().WithMessage("*append-only*");
            (await EntriesAsync("tst_order", order.Id)).Single().Action.Should().Be(AuditActions.Create);
        }
    }
}
