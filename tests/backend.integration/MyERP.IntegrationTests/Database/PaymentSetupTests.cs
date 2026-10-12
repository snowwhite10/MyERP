using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Domain;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.IntegrationTests.Database;

public sealed class PaymentSetupTests(TestDatabase database)
{
    private MyErpDbContext Context(long? companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(new DateTime(2026, 10, 12)), new FixedUser(7), company);
        return new MyErpDbContext(
            new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new PaymentSetupDbModel()], company);
    }

    private static SettlementMethodService Methods(MyErpDbContext db, params string[] usedCodes) =>
        new(db, usedCodes.Length == 0 ? [] : [new FixedUsage(usedCodes)]);

    private static PaymentTermService Terms(MyErpDbContext db, params string[] usedCodes) =>
        new(db, usedCodes.Length == 0 ? [] : [new FixedUsage(usedCodes)]);

    private static PaymentTermRule Net(int days) => new(PaymentTermDueType.NetDays, days, null, null, null);

    private static PaymentTermRule Closing(int closingDay, int days) =>
        new(PaymentTermDueType.ClosingDays, days, closingDay, null, null);

    private static PaymentTermRule MonthDay(int closingDay, int months, int paymentDay) =>
        new(PaymentTermDueType.ClosingMonthDay, null, closingDay, months, paymentDay);

    [Fact]
    public async Task SettlementMethodsAreCompanyScopedAndProtectedWhenUsed()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(41000000, 42000000);
        await using var db = Context(companyId);
        await using var otherDb = Context(companyId + 1);
        var service = Methods(db);
        var cash = await service.CreateAsync(" cash ", " Cash ", token);
        Assert.Equal(("CASH", "Cash", true, 7L), (cash.Code, cash.Name, cash.IsActive, cash.CreatedBy));
        var cheque = await service.CreateAsync("CHEQUE", "Cheque", token);
        Assert.Equal("MD.SETTLEMENT_METHOD.DUPLICATE_CODE", (await Assert.ThrowsAsync<ConflictException>(
            () => service.CreateAsync("cash", "Again", token))).ErrorCode);
        Assert.Equal("name", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
            () => service.CreateAsync("WIRE", " ", token))).Errors).Key);

        var page = await service.ListAsync(new() { Keyword = "che" }, token);
        Assert.Equal(["CHEQUE"], page.Items.Select(x => x.Code));
        Assert.Empty((await Methods(otherDb).ListAsync(new(), token)).Items);
        await Assert.ThrowsAsync<NotFoundException>(() => Methods(otherDb).UpdateAsync(cash.Id, "X", true, cash.Version, token));

        cheque = await service.UpdateAsync(cheque.Id, "Cheque / draft", false, cheque.Version, token);
        Assert.Equal(("Cheque / draft", false), (cheque.Name, cheque.IsActive));
        await Assert.ThrowsAsync<ConflictException>(() => service.UpdateAsync(cheque.Id, "Stale", true, cheque.Version - 1, token));

        var lookup = new SettlementMethodLookup(db);
        Assert.Equal(new SettlementMethodInfo("CASH", "Cash", true), await lookup.FindAsync(" cash ", token));
        Assert.Null(await lookup.FindAsync("NONE", token));
        Assert.Equal("CASH", (await lookup.RequireActiveAsync("CASH", token)).Code);
        Assert.Equal("MD.SETTLEMENT_METHOD.INACTIVE", (await Assert.ThrowsAsync<BusinessRuleException>(
            () => lookup.RequireActiveAsync("CHEQUE", token))).ErrorCode);
        Assert.Equal("MD.SETTLEMENT_METHOD.NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(
            () => lookup.RequireActiveAsync("NONE", token))).ErrorCode);
        Assert.Null(await new SettlementMethodLookup(otherDb).FindAsync("CASH", token));

        Assert.Equal("MD.SETTLEMENT_METHOD.IN_USE", (await Assert.ThrowsAsync<ConflictException>(
            () => Methods(db, "CASH").DeleteAsync(cash.Id, cash.Version, token))).ErrorCode);
        await service.DeleteAsync(cash.Id, cash.Version, token);
        Assert.Null(await lookup.FindAsync("CASH", token));
        Assert.Equal("MD.SETTLEMENT_METHOD.DUPLICATE_CODE", (await Assert.ThrowsAsync<ConflictException>(
            () => service.CreateAsync("CASH", "Cash again", token))).ErrorCode);
        Assert.Equal("MD.SETTLEMENT_METHOD.NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(
            () => service.DeleteAsync(cash.Id, cash.Version, token))).ErrorCode);

        await using var noCompany = Context(null);
        await Assert.ThrowsAsync<ForbiddenException>(() => Methods(noCompany).ListAsync(new(), token));
        await Assert.ThrowsAsync<ForbiddenException>(() => new SettlementMethodLookup(noCompany).FindAsync("CASH", token));
    }

    [Fact]
    public async Task PaymentTermsStoreOnlyTheFieldsOfTheirRule()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(42000000, 43000000);
        await using var db = Context(companyId);
        var service = Terms(db);
        var net = await service.CreateAsync("net30", "Net 30", new(PaymentTermDueType.NetDays, 30, 25, 1, 10), token);
        Assert.Equal(("NET30", "NetDays", 30, (int?)null, (int?)null, (int?)null),
            (net.Code, net.DueType, net.Days, net.ClosingDay, net.Months, net.PaymentDay));
        var monthly = await service.CreateAsync("M30", "Month-end 30", Closing(31, 30), token);
        Assert.Equal(("ClosingDays", 30, 31, (int?)null), (monthly.DueType, monthly.Days, monthly.ClosingDay, monthly.Months));
        var nextTenth = await service.CreateAsync("C25N10", "Close 25th, pay 10th next month", MonthDay(25, 1, 10), token);
        Assert.Equal(((int?)null, 25, 1, 10), (nextTenth.Days, nextTenth.ClosingDay, nextTenth.Months, nextTenth.PaymentDay));
        Assert.Equal("MD.PAYMENT_TERM.DUPLICATE_CODE", (await Assert.ThrowsAsync<ConflictException>(
            () => service.CreateAsync("NET30", "Again", Net(30), token))).ErrorCode);

        // 修改可換規則類型，不相關欄位清空。
        net = await service.UpdateAsync(net.Id, "Close 20th, pay month-end", MonthDay(20, 0, 31), true, net.Version, token);
        Assert.Equal(("ClosingMonthDay", (int?)null, 20, 0, 31), (net.DueType, net.Days, net.ClosingDay, net.Months, net.PaymentDay));
        monthly = await service.UpdateAsync(monthly.Id, "Net 0", Net(0), false, monthly.Version, token);
        Assert.Equal(("NetDays", 0, (int?)null, false), (monthly.DueType, monthly.Days, monthly.ClosingDay, monthly.IsActive));
        await Assert.ThrowsAsync<ConflictException>(
            () => service.UpdateAsync(monthly.Id, "Stale", Net(1), true, monthly.Version - 1, token));

        var page = await service.ListAsync(new() { Sort = "-code" }, token);
        Assert.Equal(["NET30", "M30", "C25N10"], page.Items.Select(x => x.Code));
        await using var otherDb = Context(companyId + 1);
        Assert.Empty((await Terms(otherDb).ListAsync(new(), token)).Items);
        await Assert.ThrowsAsync<NotFoundException>(() => Terms(otherDb).DeleteAsync(net.Id, net.Version, token));

        Assert.Equal("MD.PAYMENT_TERM.IN_USE", (await Assert.ThrowsAsync<ConflictException>(
            () => Terms(db, "NET30").DeleteAsync(net.Id, net.Version, token))).ErrorCode);
        // 已使用的條件仍可修改規則；單據保存自己的到期日（ADR-040）。
        net = await Terms(db, "NET30").UpdateAsync(net.Id, "Net 45", Net(45), true, net.Version, token);
        Assert.Equal(45, net.Days);
        await service.DeleteAsync(net.Id, net.Version, token);
        Assert.Equal(["C25N10", "M30"], (await service.ListAsync(new(), token)).Items.Select(x => x.Code));
    }

    [Theory]
    [InlineData("NetDays", null, null, null, null, "days")]
    [InlineData("NetDays", -1, null, null, null, "days")]
    [InlineData("NetDays", 366, null, null, null, "days")]
    [InlineData("ClosingDays", 30, null, null, null, "closingDay")]
    [InlineData("ClosingDays", 30, 0, null, null, "closingDay")]
    [InlineData("ClosingDays", 30, 32, null, null, "closingDay")]
    [InlineData("ClosingDays", null, 25, null, null, "days")]
    [InlineData("ClosingMonthDay", null, 25, null, 10, "months")]
    [InlineData("ClosingMonthDay", null, 25, 13, 10, "months")]
    [InlineData("ClosingMonthDay", null, 25, -1, 10, "months")]
    [InlineData("ClosingMonthDay", null, 25, 1, null, "paymentDay")]
    [InlineData("ClosingMonthDay", null, 25, 1, 0, "paymentDay")]
    [InlineData("ClosingMonthDay", null, 25, 1, 32, "paymentDay")]
    [InlineData("ClosingMonthDay", null, 25, 0, 25, "paymentDay")]
    [InlineData("ClosingMonthDay", null, 31, 0, 31, "paymentDay")]
    [InlineData("Weekly", 7, null, null, null, "dueType")]
    public void InvalidRulesAreRejected(string dueType, int? days, int? closingDay, int? months, int? paymentDay, string field)
    {
        var type = Enum.TryParse<PaymentTermDueType>(dueType, out var parsed) ? parsed : (PaymentTermDueType)99;
        var error = Assert.Throws<RequestValidationException>(
            () => PaymentTermService.Normalize(new PaymentTermRule(type, days, closingDay, months, paymentDay)));
        Assert.Equal(field, Assert.Single(error.Errors).Key);
    }

    [Theory]
    // 單據日後 N 天。
    [InlineData("NetDays", 0, null, null, null, "2026-10-12", "2026-10-12")]
    [InlineData("NetDays", 30, null, null, null, "2026-01-31", "2026-03-02")]
    // 月結 N 天：月底結帳後 N 天。
    [InlineData("ClosingDays", 30, 31, null, null, "2026-01-01", "2026-03-02")]
    [InlineData("ClosingDays", 30, 31, null, null, "2026-02-28", "2026-03-30")]
    [InlineData("ClosingDays", 60, 31, null, null, "2024-02-10", "2024-04-29")]
    // 每月 25 日結帳：25 日當天屬本期，26 日起屬下期。
    [InlineData("ClosingDays", 0, 25, null, null, "2026-10-25", "2026-10-25")]
    [InlineData("ClosingDays", 10, 25, null, null, "2026-10-26", "2026-12-05")]
    // 結帳日大於當月天數時取月底。
    [InlineData("ClosingDays", 0, 30, null, null, "2026-02-27", "2026-02-28")]
    [InlineData("ClosingDays", 0, 30, null, null, "2026-03-01", "2026-03-30")]
    // 結帳後第 M 個月的 D 日；D 大於當月天數時取月底。
    [InlineData("ClosingMonthDay", null, 25, 1, 10, "2026-10-25", "2026-11-10")]
    [InlineData("ClosingMonthDay", null, 25, 1, 10, "2026-10-26", "2026-12-10")]
    [InlineData("ClosingMonthDay", null, 31, 1, 31, "2026-01-15", "2026-02-28")]
    [InlineData("ClosingMonthDay", null, 31, 2, 5, "2026-12-31", "2027-02-05")]
    [InlineData("ClosingMonthDay", null, 20, 0, 31, "2026-04-20", "2026-04-30")]
    [InlineData("ClosingMonthDay", null, 20, 0, 31, "2026-04-21", "2026-05-31")]
    public void DueDatesFollowTheRule(string dueType, int? days, int? closingDay, int? months, int? paymentDay,
        string documentDate, string expected)
    {
        var term = new PaymentTerm
        {
            CompanyId = 1, Code = "T", Name = "T", DueType = dueType, Days = days, ClosingDay = closingDay, Months = months,
            PaymentDay = paymentDay,
        };
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        Assert.Equal(DateOnly.Parse(expected, culture), term.DueDate(DateOnly.Parse(documentDate, culture)));
    }

    [Fact]
    public async Task LookupCalculatesDueDatesForActiveTerms()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(43000000, 44000000);
        await using var db = Context(companyId);
        var service = Terms(db);
        await service.CreateAsync("M30", "Month-end 30", Closing(31, 30), token);
        var old = await service.CreateAsync("OLD", "Old", Net(7), token);
        await service.UpdateAsync(old.Id, "Old", Net(7), false, old.Version, token);
        var lookup = new PaymentTermLookup(db);
        Assert.Equal(new PaymentTermInfo("M30", "Month-end 30", PaymentTermDueType.ClosingDays, 30, 31, null, null, true),
            await lookup.FindAsync(" m30 ", token));
        Assert.False((await lookup.FindAsync("OLD", token))!.IsActive);
        Assert.Null(await lookup.FindAsync("NONE", token));
        Assert.Equal(new DateOnly(2026, 11, 30), await lookup.CalculateDueDateAsync("M30", new DateOnly(2026, 10, 12), token));
        Assert.Equal("MD.PAYMENT_TERM.INACTIVE", (await Assert.ThrowsAsync<BusinessRuleException>(
            () => lookup.CalculateDueDateAsync("OLD", new DateOnly(2026, 10, 12), token))).ErrorCode);
        Assert.Equal("MD.PAYMENT_TERM.NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(
            () => lookup.CalculateDueDateAsync("NONE", new DateOnly(2026, 10, 12), token))).ErrorCode);
        Assert.Equal("documentDate", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
            () => lookup.CalculateDueDateAsync("M30", DateOnly.MaxValue, token))).Errors).Key);
        await using var otherDb = Context(companyId + 1);
        Assert.Null(await new PaymentTermLookup(otherDb).FindAsync("M30", token));
    }

    [Fact]
    public async Task PaymentSetupTablesHaveRuleChecks()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(44000000, 45000000);
        await using var db = Context(companyId);
        db.Add(new PaymentTerm { CompanyId = companyId, Code = "BAD", Name = "Bad", DueType = "NetDays", Days = 30, ClosingDay = 25 });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(token));
        Assert.Equal(547, Assert.IsType<SqlException>(error.InnerException).Number);
        var checks = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID('md_payment_term')").ToListAsync(token);
        Assert.Contains("ck_md_payment_term_due_type", checks);
        Assert.Contains("ck_md_payment_term_rule", checks);
        Assert.Contains("ck_md_payment_term_ranges", checks);
        var indexes = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.indexes WHERE object_id IN (OBJECT_ID('md_payment_term'), OBJECT_ID('md_settlement_method'))")
            .ToListAsync(token);
        Assert.Contains("ux_md_payment_term_company_id_code", indexes);
        Assert.Contains("ux_md_settlement_method_company_id_code", indexes);
    }

    [Fact]
    public void DueTypeTravelsAsNameInJson()
    {
        var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        Assert.Equal("\"ClosingMonthDay\"", System.Text.Json.JsonSerializer.Serialize(PaymentTermDueType.ClosingMonthDay, options));
        Assert.Equal(PaymentTermDueType.NetDays, System.Text.Json.JsonSerializer.Deserialize<PaymentTermDueType>("\"NetDays\"", options));
    }

    private sealed class FixedUsage(IEnumerable<string> codes) : IPaymentTermUsage, ISettlementMethodUsage
    {
        private readonly HashSet<string> used = [.. codes];

        public Task<bool> IsInUseAsync(string code, CancellationToken token = default) => Task.FromResult(used.Contains(code));
    }
}
