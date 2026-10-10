using Microsoft.EntityFrameworkCore;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.IntegrationTests.Database;

/// <summary>共用核心（WU-P0-007）對真實 SQL Server 生效：審計蓋章、樂觀鎖、公司與軟刪除篩選。</summary>
public sealed class PersistenceTests(TestDatabase database)
{
    private static readonly DateTime Now = new(2026, 10, 10, 14, 30, 15, 123, 456);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private MyErpDbContext Create(long? companyId, long? userId = 7)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(Now), new FixedUser(userId), company);
        return database.CreateContext(company, new AuditSaveChangesInterceptor(stamper));
    }

    private async Task<long> InsertOrderAsync(long companyId, string docNo)
    {
        await using var db = Create(companyId);
        var order = new TestOrder { DocNo = docNo, Amount = 1234.5678m };
        db.Add(order);
        await db.SaveChangesAsync(Ct);
        return order.Id;
    }

    [Fact]
    public async Task Insert_StampsAuditColumnsCompanyAndVersion()
    {
        var companyId = TestCompanies.Next();

        var id = await InsertOrderAsync(companyId, "SO-AUDIT");

        await using var db = Create(companyId);
        var saved = await db.Set<TestOrder>().AsNoTracking().SingleAsync(o => o.Id == id, Ct);
        saved.CompanyId.Should().Be(companyId);
        saved.CreatedBy.Should().Be(7);
        saved.CreatedAt.Should().Be(Now, "datetime2(6) keeps microseconds");
        saved.UpdatedAt.Should().BeNull();
        saved.Version.Should().Be(1);
        saved.Amount.Should().Be(1234.5678m);
    }

    [Fact]
    public async Task Update_IncrementsVersionAndStampsUpdatedColumns()
    {
        var companyId = TestCompanies.Next();
        var id = await InsertOrderAsync(companyId, "SO-UPD");

        await using (var db = Create(companyId, userId: 9))
        {
            var order = await db.Set<TestOrder>().SingleAsync(o => o.Id == id, Ct);
            order.DocNo = "SO-UPD-2";
            await db.SaveChangesAsync(Ct);
        }

        await using var check = Create(companyId);
        var saved = await check.Set<TestOrder>().AsNoTracking().SingleAsync(o => o.Id == id, Ct);
        saved.Version.Should().Be(2);
        saved.UpdatedBy.Should().Be(9);
        saved.UpdatedAt.Should().Be(Now);
        saved.CreatedBy.Should().Be(7);
    }

    [Fact]
    public async Task Update_WithStaleVersion_ThrowsConcurrencyExceptionAndKeepsRow()
    {
        var companyId = TestCompanies.Next();
        var id = await InsertOrderAsync(companyId, "SO-LOCK");

        await using (var db = Create(companyId))
        {
            var order = await db.Set<TestOrder>().SingleAsync(o => o.Id == id, Ct);
            db.Entry(order).Property(o => o.Version).OriginalValue = 0; // 用戶端帶來舊版本
            order.DocNo = "SO-LOCK-STALE";

            var act = () => db.SaveChangesAsync(Ct);

            await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
        }

        await using var check = Create(companyId);
        var saved = await check.Set<TestOrder>().AsNoTracking().SingleAsync(o => o.Id == id, Ct);
        saved.DocNo.Should().Be("SO-LOCK");
        saved.Version.Should().Be(1);
    }

    [Fact]
    public async Task CompanyFilter_ReturnsOnlyCurrentCompanyRows()
    {
        var companyA = TestCompanies.Next();
        var companyB = TestCompanies.Next();
        await InsertOrderAsync(companyA, "SO-A");
        await InsertOrderAsync(companyB, "SO-B");

        await using var db = Create(companyA);
        var docNos = await db.Set<TestOrder>().Where(o => o.CompanyId == companyA || o.CompanyId == companyB).Select(o => o.DocNo).ToListAsync(Ct);

        docNos.Should().Equal("SO-A");
    }

    [Fact]
    public async Task CompanyFilter_WithoutCompany_ReturnsNothing()
    {
        var companyId = TestCompanies.Next();
        await InsertOrderAsync(companyId, "SO-NOCO");

        await using var db = Create(companyId: null);

        (await db.Set<TestOrder>().CountAsync(Ct)).Should().Be(0);
    }

    [Fact]
    public async Task CompanyFilter_IsEvaluatedPerContextInstance()
    {
        // 同一個快取的模型，不同 DbContext 實例讀各自的公司
        var companyA = TestCompanies.Next();
        var companyB = TestCompanies.Next();
        await InsertOrderAsync(companyA, "SO-PA");
        await InsertOrderAsync(companyB, "SO-PB");

        await using var dbA = Create(companyA);
        await using var dbB = Create(companyB);

        (await dbA.Set<TestOrder>().Select(o => o.DocNo).ToListAsync(Ct)).Should().Equal("SO-PA");
        (await dbB.Set<TestOrder>().Select(o => o.DocNo).ToListAsync(Ct)).Should().Equal("SO-PB");
    }

    [Fact]
    public async Task SoftDeleteFilter_HidesDeletedMasters_IgnoringItKeepsCompanyIsolation()
    {
        var companyA = TestCompanies.Next();
        var companyB = TestCompanies.Next();
        await using (var db = Create(companyA))
        {
            db.AddRange(new TestCustomer { Code = "C1", Name = "啟用客戶" }, new TestCustomer { Code = "C2", Name = "已刪客戶", IsDeleted = true });
            await db.SaveChangesAsync(Ct);
        }

        await using (var db = Create(companyB))
        {
            db.Add(new TestCustomer { Code = "C3", Name = "他公司已刪", IsDeleted = true });
            await db.SaveChangesAsync(Ct);
        }

        await using var query = Create(companyA);
        (await query.Set<TestCustomer>().Select(c => c.Code).ToListAsync(Ct)).Should().Equal("C1");
        (await query.Set<TestCustomer>().IgnoreQueryFilters([ErpQueryFilters.SoftDelete]).OrderBy(c => c.Code).Select(c => c.Code).ToListAsync(Ct))
            .Should().Equal("C1", "C2");
    }

    [Fact]
    public async Task NVarcharColumns_RoundTripTraditionalSimplifiedAndSupplementaryCharacters()
    {
        var companyId = TestCompanies.Next();
        const string name = "臺灣台湾𠮷Ω";
        await using (var db = Create(companyId))
        {
            db.Add(new TestCustomer { Code = "U1", Name = name });
            await db.SaveChangesAsync(Ct);
        }

        await using var check = Create(companyId);
        (await check.Set<TestCustomer>().SingleAsync(c => c.Code == "u1", Ct)).Name.Should().Be(name, "collation is case-insensitive and NVARCHAR keeps all characters");
    }
}
