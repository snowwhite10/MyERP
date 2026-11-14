using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MyERP.Modules.System.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Infrastructure.Seeding;

namespace MyERP.Modules.System.Application.Seeding;

/// <summary>新增的示範單號規則筆數（全部公司合計）；重複執行應為 0。</summary>
public sealed record NumberRuleSeedResult(int Rules);

/// <summary>
/// WU-FI-005c3a（ADR-135）：SYS 單號規則種子契約。收集各模組 <see cref="ISeedNumberRuleProvider"/> 的定義，
/// 先全部驗證（單據類型、名稱、前綴、長度與週期組合、跨模組重複），任何一項不符即拋出且不寫入；
/// 再為每間種子公司建立缺少的規則。冪等：同公司同單據類型已有規則（包括已停用或已被用戶修改）即略過，不修改。
/// 在 SYS 主種子（100）之後、其他模組種子之前執行。
/// </summary>
public sealed partial class NumberRuleDemoSeeder(
    MyErpDbContext db,
    ISeedCompanyDirectory companies,
    IEnumerable<ISeedNumberRuleProvider> providers,
    ILogger<NumberRuleDemoSeeder> logger) : IDataSeeder
{
    public int Order => 150;

    public string Name => "SYS.NUMBER_RULE";

    async Task IDataSeeder.SeedAsync(CancellationToken cancellationToken) => await SeedAsync(cancellationToken);

    public async Task<NumberRuleSeedResult> SeedAsync(CancellationToken cancellationToken = default)
    {
        var plan = BuildPlan(providers);
        if (plan.Count == 0) return new NumberRuleSeedResult(0);

        var types = plan.Select(r => r.DocumentType).ToList();
        var created = 0;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var company in await companies.ListAsync(cancellationToken))
        {
            using var context = SeedCompanyContext.Enter(company.Id);
            var existing = (await db.Set<NumberRule>().IgnoreQueryFilters().AsNoTracking()
                    .Where(r => r.CompanyId == company.Id && types.Contains(r.DocumentType))
                    .Select(r => r.DocumentType)
                    .ToListAsync(cancellationToken))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var added = plan
                .Where(r => !existing.Contains(r.DocumentType))
                .Select(r => new NumberRule
                {
                    CompanyId = company.Id,
                    DocumentType = r.DocumentType,
                    Name = r.Name,
                    Prefix = r.Prefix,
                    DateFormat = r.DateFormat,
                    SequenceLength = r.SequenceLength,
                    ResetPeriod = r.ResetPeriod,
                })
                .ToList();
            db.AddRange(added);
            await db.SaveChangesAsync(cancellationToken);
            created += added.Count;
        }

        await transaction.CommitAsync(cancellationToken);
        SeedCompleted(logger, created);
        return new NumberRuleSeedResult(created);
    }

    /// <summary>已驗證、正規化的規則。</summary>
    public sealed record PlannedRule(
        string Module, string DocumentType, string Name, string Prefix, NumberDateFormat DateFormat, int SequenceLength,
        NumberResetPeriod ResetPeriod);

    /// <summary>
    /// 驗證並正規化全部定義。任何定義不符單號規則的限制，或兩個定義宣告同一單據類型（不分大小寫），
    /// 拋出 <see cref="InvalidOperationException"/>，訊息含模組與單據類型；結果依單據類型排序。
    /// </summary>
    public static IReadOnlyList<PlannedRule> BuildPlan(IEnumerable<ISeedNumberRuleProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        var plan = new Dictionary<string, PlannedRule>(StringComparer.Ordinal);
        foreach (var provider in providers)
        {
            foreach (var definition in provider.Rules)
            {
                var rule = Normalize(provider.Module, definition);
                if (plan.TryGetValue(rule.DocumentType, out var other))
                {
                    throw new InvalidOperationException(
                        $"Seed number rule '{rule.DocumentType}' is declared by both {other.Module} and {rule.Module}.");
                }

                plan.Add(rule.DocumentType, rule);
            }
        }

        return plan.Values.OrderBy(r => r.DocumentType, StringComparer.Ordinal).ToList();
    }

    private static PlannedRule Normalize(string module, SeedNumberRuleDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        try
        {
            var type = NumberRule.NormalizeDocumentType(definition.DocumentType);
            var name = definition.Name?.Trim() ?? "";
            if (name.Length is 0 or > 100)
                throw new RequestValidationException("name", "Required; maximum 100 characters.");
            var prefix = NumberRule.NormalizePrefix(definition.Prefix);
            var dateFormat = definition.DatePart switch
            {
                SeedNumberDatePart.None => NumberDateFormat.None,
                SeedNumberDatePart.Year => NumberDateFormat.Year,
                SeedNumberDatePart.YearMonth => NumberDateFormat.YearMonth,
                SeedNumberDatePart.YearMonthDay => NumberDateFormat.YearMonthDay,
                _ => throw new RequestValidationException("dateFormat", "Unknown date format."),
            };
            var reset = definition.Reset switch
            {
                SeedNumberReset.Never => NumberResetPeriod.Never,
                SeedNumberReset.Yearly => NumberResetPeriod.Yearly,
                SeedNumberReset.Monthly => NumberResetPeriod.Monthly,
                SeedNumberReset.Daily => NumberResetPeriod.Daily,
                _ => throw new RequestValidationException("resetPeriod", "Unknown reset period."),
            };
            NumberRule.ValidateSettings(dateFormat, definition.SequenceLength, reset);
            return new PlannedRule(module, type, name, prefix, dateFormat, definition.SequenceLength, reset);
        }
        catch (RequestValidationException ex)
        {
            throw new InvalidOperationException(
                $"Seed number rule '{definition.DocumentType}' from {module} is invalid: "
                + string.Join("; ", ex.Errors.Select(e => $"{e.Key}: {string.Join(" ", e.Value)}")), ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Number rule seed completed: {Created} rule(s) added")]
    private static partial void SeedCompleted(ILogger logger, int created);
}
