using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.MasterData.Application;

/// <summary>結算方式 CRUD（ADR-040）；是否已被收付款單據引用由使用方模組實作 <see cref="ISettlementMethodUsage"/> 判斷。</summary>
public sealed class SettlementMethodService(MyErpDbContext db, IEnumerable<ISettlementMethodUsage> usages)
    : MasterCategoryService<SettlementMethod>(db, "MD.SETTLEMENT_METHOD", "Settlement method")
{
    protected override SettlementMethod New(long companyId, string code, string name) =>
        new() { CompanyId = companyId, Code = code, Name = name };

    protected override async Task EnsureNotInUseAsync(long id, CancellationToken token)
    {
        if (!usages.Any()) return;
        // 刪除流程已載入並追蹤該筆資料，FindAsync 直接取自追蹤器，不再查詢資料庫。
        var code = (await Db.Set<SettlementMethod>().FindAsync([id], token))!.Code;
        foreach (var usage in usages)
            if (await usage.IsInUseAsync(code, token))
                throw new ConflictException(ErrorPrefix + ".IN_USE", "Settlement method is referenced by documents.");
    }
}

/// <summary>付款條件的到期規則輸入；不適用於 <see cref="DueType"/> 的欄位會被忽略。</summary>
public sealed record PaymentTermRule(PaymentTermDueType DueType, int? Days, int? ClosingDay, int? Months, int? PaymentDay);

/// <summary>
/// 付款條件 CRUD（ADR-040）。規則可隨時修改（單據保存自己的到期日，不回溯）；
/// 已被單據引用的條件不可刪除，由使用方模組實作 <see cref="IPaymentTermUsage"/> 判斷。
/// </summary>
public sealed class PaymentTermService(MyErpDbContext db, IEnumerable<IPaymentTermUsage> usages)
    : MasterCategoryService<PaymentTerm>(db, "MD.PAYMENT_TERM", "Payment term")
{
    public Task<PaymentTerm> CreateAsync(string? code, string? name, PaymentTermRule rule, CancellationToken token = default)
    {
        var normalized = Normalize(rule);
        return CreateCoreAsync(code, name, x => Apply(x, normalized), token);
    }

    public Task<PaymentTerm> UpdateAsync(long id, string? name, PaymentTermRule rule, bool isActive, int version,
        CancellationToken token = default)
    {
        var normalized = Normalize(rule);
        return UpdateCoreAsync(id, name, isActive, version, x => Apply(x, normalized), token);
    }

    /// <summary>驗證規則並清空不適用的欄位。</summary>
    public static PaymentTermRule Normalize(PaymentTermRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return rule.DueType switch
        {
            PaymentTermDueType.NetDays => new(rule.DueType, Days(rule.Days), null, null, null),
            PaymentTermDueType.ClosingDays => new(rule.DueType, Days(rule.Days), ClosingDay(rule.ClosingDay), null, null),
            PaymentTermDueType.ClosingMonthDay => MonthDay(rule),
            _ => throw new RequestValidationException("dueType", "Use NetDays, ClosingDays or ClosingMonthDay."),
        };
    }

    protected override PaymentTerm New(long companyId, string code, string name) =>
        new() { CompanyId = companyId, Code = code, Name = name };

    protected override async Task EnsureNotInUseAsync(long id, CancellationToken token)
    {
        if (!usages.Any()) return;
        var code = (await Db.Set<PaymentTerm>().FindAsync([id], token))!.Code;
        foreach (var usage in usages)
            if (await usage.IsInUseAsync(code, token))
                throw new ConflictException(ErrorPrefix + ".IN_USE", "Payment term is referenced by documents.");
    }

    private static PaymentTermRule MonthDay(PaymentTermRule rule)
    {
        var closingDay = ClosingDay(rule.ClosingDay);
        if (rule.Months is not (>= 0 and <= PaymentTerm.MaxMonths))
            throw new RequestValidationException("months", $"Use 0–{PaymentTerm.MaxMonths} months after the closing date.");
        var paymentDay = DayOfMonth(rule.PaymentDay, "paymentDay");
        if (rule.Months == 0 && paymentDay <= closingDay)
            throw new RequestValidationException("paymentDay", "In the closing month the payment day must be after the closing day.");
        return new(rule.DueType, null, closingDay, rule.Months, paymentDay);
    }

    private static int Days(int? days) => days is >= 0 and <= PaymentTerm.MaxDays ? days.Value
        : throw new RequestValidationException("days", $"Use 0–{PaymentTerm.MaxDays} days.");

    private static int ClosingDay(int? day) => DayOfMonth(day, "closingDay");

    private static int DayOfMonth(int? day, string field) => day is >= 1 and <= PaymentTerm.MonthEnd ? day.Value
        : throw new RequestValidationException(field, $"Use a day of month 1–{PaymentTerm.MonthEnd} ({PaymentTerm.MonthEnd} = month end).");

    private static void Apply(PaymentTerm entity, PaymentTermRule rule) =>
        (entity.DueType, entity.Days, entity.ClosingDay, entity.Months, entity.PaymentDay) =
        (rule.DueType.ToString(), rule.Days, rule.ClosingDay, rule.Months, rule.PaymentDay);
}

/// <summary><see cref="IPaymentTermLookup"/> 的實作；只讀目前公司的付款條件（ADR-040）。</summary>
public sealed class PaymentTermLookup(MyErpDbContext db) : IPaymentTermLookup
{
    public async Task<PaymentTermInfo?> FindAsync(string code, CancellationToken token = default)
    {
        var entity = await FindEntityAsync(code, token);
        return entity is null ? null : Map(entity);
    }

    public async Task<DateOnly> CalculateDueDateAsync(string code, DateOnly documentDate, CancellationToken token = default)
    {
        var entity = await FindEntityAsync(code, token)
            ?? throw new NotFoundException("MD.PAYMENT_TERM.NOT_FOUND", "Payment term", code ?? string.Empty);
        if (!entity.IsActive) throw new BusinessRuleException("MD.PAYMENT_TERM.INACTIVE", "Payment term is inactive.");
        try { return entity.DueDate(documentDate); }
        catch (ArgumentOutOfRangeException) { throw new RequestValidationException("documentDate", "The due date is out of range."); }
    }

    private static PaymentTermInfo Map(PaymentTerm x) => new(x.Code, x.Name, Enum.Parse<PaymentTermDueType>(x.DueType), x.Days,
        x.ClosingDay, x.Months, x.PaymentDay, x.IsActive);

    private Task<PaymentTerm?> FindEntityAsync(string code, CancellationToken token)
    {
        RequireCompany(db);
        var normalized = code?.Trim().ToUpperInvariant();
        return db.Set<PaymentTerm>().AsNoTracking().SingleOrDefaultAsync(x => x.Code == normalized, token);
    }

    internal static void RequireCompany(MyErpDbContext db)
    {
        if (db.CurrentCompanyId is not > 0)
            throw new ForbiddenException("COMMON.COMPANY.ACCESS_DENIED", "Current company is required.");
    }
}

/// <summary><see cref="ISettlementMethodLookup"/> 的實作；只讀目前公司的結算方式（ADR-040）。</summary>
public sealed class SettlementMethodLookup(MyErpDbContext db) : ISettlementMethodLookup
{
    public async Task<SettlementMethodInfo?> FindAsync(string code, CancellationToken token = default)
    {
        PaymentTermLookup.RequireCompany(db);
        var normalized = code?.Trim().ToUpperInvariant();
        return await db.Set<SettlementMethod>().AsNoTracking().Where(x => x.Code == normalized)
            .Select(x => new SettlementMethodInfo(x.Code, x.Name, x.IsActive)).SingleOrDefaultAsync(token);
    }

    public async Task<SettlementMethodInfo> RequireActiveAsync(string code, CancellationToken token = default)
    {
        var method = await FindAsync(code, token)
            ?? throw new NotFoundException("MD.SETTLEMENT_METHOD.NOT_FOUND", "Settlement method", code ?? string.Empty);
        return method.IsActive ? method
            : throw new BusinessRuleException("MD.SETTLEMENT_METHOD.INACTIVE", "Settlement method is inactive.");
    }
}
