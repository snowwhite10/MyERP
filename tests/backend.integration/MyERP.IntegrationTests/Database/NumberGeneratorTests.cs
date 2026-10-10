using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.System.Application.Numbering;
using MyERP.Modules.System.Domain;
using MyERP.Modules.System.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;

namespace MyERP.IntegrationTests.Database;

/// <summary>SYS-NUM-001～007：單號產生服務的併發、回滾、週期重設、公司隔離與規則維護（myerp_test）。</summary>
public sealed class NumberGeneratorTests(TestDatabase database)
{
    private static readonly FixedClock Clock = new(new DateTime(2026, 10, 11, 9, 30, 0));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ParallelGenerationFromManyContextsNeverDuplicatesOrSkips()
    {
        var companyId = await CreateCompanyAsync();
        await CreateRuleAsync(companyId, "TST.PARALLEL", "P", NumberDateFormat.None, 4, NumberResetPeriod.Never);
        var numbers = new ConcurrentBag<string>();

        await Parallel.ForEachAsync(Enumerable.Range(0, 100), new ParallelOptions { MaxDegreeOfParallelism = 16, CancellationToken = Ct }, async (i, token) =>
        {
            await using var db = Context(companyId);
            var generator = new NumberGenerator(db, Clock, new FixedUser(7));
            if (i % 2 == 0)
            {
                // 單據交易內取號：計數列鎖定至提交
                await using var transaction = await db.Database.BeginTransactionAsync(token);
                numbers.Add(await generator.NextAsync("tst.parallel", new DateOnly(2026, 10, 11), token));
                await transaction.CommitAsync(token);
            }
            else
            {
                numbers.Add(await generator.NextAsync("TST.PARALLEL", new DateOnly(2026, 10, 11), token));
            }
        });

        Assert.Equal(Enumerable.Range(1, 100).Select(n => $"P{n:0000}"), numbers.Order(StringComparer.Ordinal));
        await using var check = Context(companyId);
        var sequence = await check.Set<NumberSequence>().AsNoTracking().SingleAsync(Ct);
        Assert.Equal(100, sequence.CurrentValue);
        Assert.Equal("-", sequence.PeriodKey);
        Assert.Equal(7, sequence.UpdatedBy);
        Assert.Equal(100, sequence.Version);
    }

    [Fact]
    public async Task RolledBackDocumentTransactionReturnsTheNumber()
    {
        var companyId = await CreateCompanyAsync();
        await CreateRuleAsync(companyId, "TST.ROLLBACK", "RB-", NumberDateFormat.None, 3, NumberResetPeriod.Never);
        var date = new DateOnly(2026, 10, 11);

        await using (var db = Context(companyId))
        {
            await using var transaction = await db.Database.BeginTransactionAsync(Ct);
            Assert.Equal("RB-001", await Generator(db).NextAsync("TST.ROLLBACK", date, Ct));
            await transaction.RollbackAsync(Ct);
        }

        await using (var db = Context(companyId))
        {
            await using var transaction = await db.Database.BeginTransactionAsync(Ct);
            Assert.Equal("RB-001", await Generator(db).NextAsync("TST.ROLLBACK", date, Ct));
            Assert.Equal("RB-002", await Generator(db).NextAsync("TST.ROLLBACK", date, Ct));
            await transaction.CommitAsync(Ct);
        }

        await using var after = Context(companyId);
        Assert.Equal("RB-003", await Generator(after).NextAsync("TST.ROLLBACK", date, Ct));
    }

    [Fact]
    public async Task ResetPeriodsRestartTheSequenceByDocumentDate()
    {
        var companyId = await CreateCompanyAsync();
        await CreateRuleAsync(companyId, "TST.MONTHLY", "SO", NumberDateFormat.YearMonth, 4, NumberResetPeriod.Monthly);
        await CreateRuleAsync(companyId, "TST.YEARLY", "PO", NumberDateFormat.ShortYearMonth, 3, NumberResetPeriod.Yearly);
        await CreateRuleAsync(companyId, "TST.DAILY", "", NumberDateFormat.YearMonthDay, 3, NumberResetPeriod.Daily);
        await CreateRuleAsync(companyId, "TST.NEVER", "GR", NumberDateFormat.Year, 5, NumberResetPeriod.Never);
        await using var db = Context(companyId);
        var generator = Generator(db);

        Assert.Equal("SO2026100001", await generator.NextAsync("TST.MONTHLY", new DateOnly(2026, 10, 31), Ct));
        Assert.Equal("SO2026100002", await generator.NextAsync("TST.MONTHLY", new DateOnly(2026, 10, 1), Ct));
        Assert.Equal("SO2026110001", await generator.NextAsync("TST.MONTHLY", new DateOnly(2026, 11, 1), Ct));
        // 補登上月單據沿用上月的計數
        Assert.Equal("SO2026100003", await generator.NextAsync("TST.MONTHLY", new DateOnly(2026, 10, 15), Ct));

        Assert.Equal("PO2612001", await generator.NextAsync("TST.YEARLY", new DateOnly(2026, 12, 31), Ct));
        Assert.Equal("PO2701001", await generator.NextAsync("TST.YEARLY", new DateOnly(2027, 1, 1), Ct));
        Assert.Equal("PO2702002", await generator.NextAsync("TST.YEARLY", new DateOnly(2027, 2, 1), Ct));

        Assert.Equal("20261011001", await generator.NextAsync("TST.DAILY", new DateOnly(2026, 10, 11), Ct));
        Assert.Equal("20261012001", await generator.NextAsync("TST.DAILY", new DateOnly(2026, 10, 12), Ct));

        Assert.Equal("GR202600001", await generator.NextAsync("TST.NEVER", new DateOnly(2026, 12, 31), Ct));
        Assert.Equal("GR202700002", await generator.NextAsync("TST.NEVER", new DateOnly(2027, 1, 1), Ct));

        var periods = await db.Set<NumberSequence>().AsNoTracking().Select(s => s.PeriodKey).ToListAsync(Ct);
        Assert.Equal(["-", "2026", "202610", "20261011", "20261012", "202611", "2027"], periods.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task CompaniesHaveSeparateRulesAndSequences()
    {
        var companyA = await CreateCompanyAsync();
        var companyB = await CreateCompanyAsync();
        var companyC = await CreateCompanyAsync();
        await CreateRuleAsync(companyA, "TST.ORDER", "A", NumberDateFormat.None, 3, NumberResetPeriod.Never);
        await CreateRuleAsync(companyB, "TST.ORDER", "B", NumberDateFormat.None, 3, NumberResetPeriod.Never);
        var inactive = await CreateRuleAsync(companyB, "TST.OLD", "O", NumberDateFormat.None, 3, NumberResetPeriod.Never);
        var date = new DateOnly(2026, 10, 11);

        await using (var db = Context(companyA))
        {
            Assert.Equal("A001", await Generator(db).NextAsync("TST.ORDER", date, Ct));
            Assert.Equal("A002", await Generator(db).NextAsync("TST.ORDER", date, Ct));
            var missing = await Assert.ThrowsAsync<BusinessRuleException>(() => Generator(db).NextAsync("TST.OLD", date, Ct));
            Assert.Equal(NumberGenerator.NotConfigured, missing.ErrorCode);
        }

        await using (var db = Context(companyB))
        {
            Assert.Equal("B001", await Generator(db).NextAsync("TST.ORDER", date, Ct));
            var rule = await db.Set<NumberRule>().SingleAsync(r => r.Id == inactive, Ct);
            rule.IsActive = false;
            await db.SaveChangesAsync(Ct);
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Generator(db).NextAsync("TST.OLD", date, Ct));
            Assert.Equal(NumberGenerator.Inactive, error.ErrorCode);
        }

        await using (var db = Context(companyC))
        {
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Generator(db).NextAsync("TST.ORDER", date, Ct));
            Assert.Equal(NumberGenerator.NotConfigured, error.ErrorCode);
        }

        await using (var db = Context(null))
        {
            await Assert.ThrowsAsync<ForbiddenException>(() => Generator(db).NextAsync("TST.ORDER", date, Ct));
            await Assert.ThrowsAsync<RequestValidationException>(() => Generator(db).NextAsync("order", date, Ct));
        }
    }

    [Fact]
    public async Task ExhaustedSequenceIsRejectedAndDatabaseGuardsSequenceRows()
    {
        var companyId = await CreateCompanyAsync();
        var otherCompany = await CreateCompanyAsync();
        var ruleId = await CreateRuleAsync(companyId, "TST.SMALL", "S", NumberDateFormat.None, 3, NumberResetPeriod.Never);
        await using var db = Context(companyId);
        db.Add(new NumberSequence { CompanyId = companyId, RuleId = ruleId, PeriodKey = "-", CurrentValue = 998 });
        await db.SaveChangesAsync(Ct);
        db.ChangeTracker.Clear();

        Assert.Equal("S999", await Generator(db).NextAsync("TST.SMALL", new DateOnly(2026, 10, 11), Ct));
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Generator(db).NextAsync("TST.SMALL", new DateOnly(2026, 10, 11), Ct));
        Assert.Equal("SYS.NUMBER_RULE.SEQUENCE_EXHAUSTED", error.ErrorCode);

        // 計數列不可指向他公司的規則、同一週期不可有兩列、流水號至少為 1
        foreach (var invalid in new[]
        {
            new NumberSequence { CompanyId = otherCompany, RuleId = ruleId, PeriodKey = "2026", CurrentValue = 1 },
            new NumberSequence { CompanyId = companyId, RuleId = ruleId, PeriodKey = "-", CurrentValue = 1 },
            new NumberSequence { CompanyId = companyId, RuleId = ruleId, PeriodKey = "2027", CurrentValue = 0 },
        })
        {
            db.Add(invalid);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
            db.ChangeTracker.Clear();
        }

        db.Add(new NumberRule
        {
            CompanyId = companyId, DocumentType = "TST.SMALL", Name = "Duplicate", Prefix = "", SequenceLength = 3,
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
        db.ChangeTracker.Clear();
        db.Add(new NumberRule { CompanyId = companyId, DocumentType = "TST.LONG", Name = "Too long", Prefix = "", SequenceLength = 11 });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task RuleServiceCreatesListsPreviewsAndLocksIssuedRules()
    {
        var companyId = await CreateCompanyAsync();
        var otherCompany = await CreateCompanyAsync();
        await using var db = Context(companyId);
        var service = new NumberRuleService(db, Clock);

        var created = await service.CreateAsync(new NumberRuleInput(" tst.invoice ", " Invoice ", " inv- ", NumberDateFormat.YearMonth, 4, NumberResetPeriod.Monthly), Ct);
        Assert.Equal("TST.INVOICE", created.DocumentType);
        Assert.Equal("Invoice", created.Name);
        Assert.Equal("INV-", created.Prefix);
        Assert.Equal("INV-2026100001", created.Sample);
        Assert.False(created.IsInUse);
        Assert.Equal(1, created.Version);
        var duplicate = await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(
            new NumberRuleInput("TST.INVOICE", "Again", null, NumberDateFormat.None, 4, NumberResetPeriod.Never), Ct));
        Assert.Equal(NumberRuleService.DuplicateType, duplicate.ErrorCode);
        await service.CreateAsync(new NumberRuleInput("TST.ALPHA", "Alpha", null, NumberDateFormat.None, 5, NumberResetPeriod.Never), Ct);

        var page = await service.ListAsync(new PagedRequest { Sort = "-documentType" }, Ct);
        Assert.Equal(["TST.INVOICE", "TST.ALPHA"], page.Items.Select(r => r.DocumentType));
        Assert.Equal(1, (await service.ListAsync(new PagedRequest { Keyword = "alpha" }, Ct)).TotalCount);
        await Assert.ThrowsAsync<RequestValidationException>(() => service.ListAsync(new PagedRequest { Sort = "prefix" }, Ct));

        // 預覽不佔用流水號
        var preview = await service.PreviewAsync(created.Id, new DateOnly(2026, 11, 3), Ct);
        Assert.Equal("INV-2026110001", preview.NextNumber);
        Assert.Equal("INV-2026100001", (await service.PreviewAsync(created.Id, null, Ct)).NextNumber);
        Assert.Equal("INV-2026100001", await Generator(db).NextAsync("TST.INVOICE", Clock.Today, Ct));
        Assert.Equal("INV-2026100002", (await service.PreviewAsync(created.Id, null, Ct)).NextNumber);

        // 已發出單號：可改名稱、前綴、加長流水號、停用；不可改格式或週期、不可縮短
        var detail = await service.GetAsync(created.Id, Ct);
        Assert.True(detail.IsInUse);
        foreach (var locked in new[]
        {
            new NumberRuleUpdate("Invoice", "INV-", NumberDateFormat.YearMonthDay, 4, NumberResetPeriod.Monthly, true, detail.Version),
            new NumberRuleUpdate("Invoice", "INV-", NumberDateFormat.YearMonth, 4, NumberResetPeriod.Never, true, detail.Version),
            new NumberRuleUpdate("Invoice", "INV-", NumberDateFormat.YearMonth, 3, NumberResetPeriod.Monthly, true, detail.Version),
        })
        {
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() => service.UpdateAsync(created.Id, locked, Ct));
            Assert.Equal(NumberRuleService.InUse, error.ErrorCode);
        }

        var updated = await service.UpdateAsync(created.Id,
            new NumberRuleUpdate("Sales invoice", "si", NumberDateFormat.YearMonth, 6, NumberResetPeriod.Monthly, false, detail.Version), Ct);
        Assert.Equal("SI202610000001", updated.Sample);
        Assert.False(updated.IsActive);
        Assert.Equal(detail.Version + 1, updated.Version);
        await Assert.ThrowsAsync<ConflictException>(() => service.UpdateAsync(created.Id,
            new NumberRuleUpdate("Stale", "SI", NumberDateFormat.YearMonth, 6, NumberResetPeriod.Monthly, true, detail.Version), Ct));

        // 他公司的規則看不到
        await using var other = Context(otherCompany);
        var otherService = new NumberRuleService(other, Clock);
        await Assert.ThrowsAsync<NotFoundException>(() => otherService.GetAsync(created.Id, Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => otherService.PreviewAsync(created.Id, null, Ct));
        Assert.Equal(0, (await otherService.ListAsync(new PagedRequest(), Ct)).TotalCount);
        await using var none = Context(null);
        await Assert.ThrowsAsync<ForbiddenException>(() => new NumberRuleService(none, Clock).ListAsync(new PagedRequest(), Ct));
    }

    [Fact]
    public async Task RuleServiceRejectsInvalidSettings()
    {
        var companyId = await CreateCompanyAsync();
        await using var db = Context(companyId);
        var service = new NumberRuleService(db, Clock);

        foreach (var (input, field) in new[]
        {
            (new NumberRuleInput("ORDER", "Order", null, NumberDateFormat.None, 4, NumberResetPeriod.Never), "documentType"),
            (new NumberRuleInput("TST.X", " ", null, NumberDateFormat.None, 4, NumberResetPeriod.Never), "name"),
            (new NumberRuleInput("TST.X", "X", "TOO-LONG-PREFIX", NumberDateFormat.None, 4, NumberResetPeriod.Never), "prefix"),
            (new NumberRuleInput("TST.X", "X", "S O", NumberDateFormat.None, 4, NumberResetPeriod.Never), "prefix"),
            (new NumberRuleInput("TST.X", "X", null, NumberDateFormat.None, 2, NumberResetPeriod.Never), "sequenceLength"),
            (new NumberRuleInput("TST.X", "X", null, NumberDateFormat.None, 4, NumberResetPeriod.Yearly), "resetPeriod"),
            (new NumberRuleInput("TST.X", "X", null, NumberDateFormat.Year, 4, NumberResetPeriod.Monthly), "resetPeriod"),
            (new NumberRuleInput("TST.X", "X", null, NumberDateFormat.ShortYearMonth, 4, NumberResetPeriod.Daily), "resetPeriod"),
            (new NumberRuleInput("TST.X", "X", null, (NumberDateFormat)99, 4, NumberResetPeriod.Never), "dateFormat"),
            (new NumberRuleInput("TST.X", "X", null, NumberDateFormat.None, 4, (NumberResetPeriod)99), "resetPeriod"),
        })
        {
            var error = await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync(input, Ct));
            Assert.Contains(field, error.Errors.Keys);
        }

        await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateAsync(long.MaxValue,
            new NumberRuleUpdate("X", null, NumberDateFormat.None, 4, NumberResetPeriod.Never, true, 1), Ct));
    }

    private MyErpDbContext Context(long? companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(Clock, new FixedUser(null), company);
        return new(new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new SystemOrganizationDbModel()], company);
    }

    private static NumberGenerator Generator(MyErpDbContext db) => new(db, Clock, new FixedUser(null));

    private async Task<long> CreateCompanyAsync()
    {
        await using var db = Context(null);
        var company = new SystemCompany { Code = "N" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), Name = "Numbering" };
        db.Add(company);
        await db.SaveChangesAsync(Ct);
        return company.Id;
    }

    private async Task<long> CreateRuleAsync(
        long companyId, string documentType, string prefix, NumberDateFormat format, int length, NumberResetPeriod reset)
    {
        await using var db = Context(companyId);
        var rule = await new NumberRuleService(db, Clock).CreateAsync(new NumberRuleInput(documentType, documentType, prefix, format, length, reset), Ct);
        return rule.Id;
    }
}
