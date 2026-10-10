using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.System.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Numbering;
using MyERP.Shared.Security;
using MyERP.Shared.Time;

namespace MyERP.Modules.System.Application.Numbering;

/// <summary>
/// <see cref="INumberGenerator"/> 的 SYS 實作（REQ_F4118.28.5、ADR-021）。流水號以單一 <c>MERGE … WITH (HOLDLOCK)</c>
/// 原子遞增或建立：同一規則同一週期的並行取號在計數列（或其鍵範圍）上排隊，不會讀到相同的值。
/// 語句在 DbContext 目前的交易內執行，交易回滾即退回流水號。
/// </summary>
public sealed class NumberGenerator(MyErpDbContext db, IClock clock, ICurrentUser user) : INumberGenerator
{
    public const string NotConfigured = "SYS.NUMBER_RULE.NOT_CONFIGURED";
    public const string Inactive = "SYS.NUMBER_RULE.INACTIVE";

    private const string NextValueSql = """
        DECLARE @result TABLE (value BIGINT NOT NULL);
        MERGE dbo.sys_number_sequence WITH (HOLDLOCK) AS t
        USING (SELECT @company_id AS company_id, @rule_id AS rule_id, @period_key AS period_key) AS s
            ON t.company_id = s.company_id AND t.rule_id = s.rule_id AND t.period_key = s.period_key
        WHEN MATCHED THEN
            UPDATE SET current_value = t.current_value + 1, updated_at = @now, updated_by = @user_id, version = t.version + 1
        WHEN NOT MATCHED THEN
            INSERT (company_id, rule_id, period_key, current_value, created_at, created_by, version)
            VALUES (s.company_id, s.rule_id, s.period_key, 1, @now, @user_id, 1)
        OUTPUT inserted.current_value INTO @result;
        SELECT @next = value FROM @result;
        """;

    public async Task<string> NextAsync(string documentType, DateOnly documentDate, CancellationToken cancellationToken = default)
    {
        var type = NumberRule.NormalizeDocumentType(documentType);
        var companyId = db.CurrentCompanyId
            ?? throw new ForbiddenException("SYS.COMPANY.UNAVAILABLE", "An active company context is required.");
        var rule = await db.Set<NumberRule>().AsNoTracking().SingleOrDefaultAsync(r => r.DocumentType == type, cancellationToken)
            ?? throw new BusinessRuleException(NotConfigured, $"No number rule is configured for '{type}'.");
        if (!rule.IsActive)
            throw new BusinessRuleException(Inactive, $"The number rule for '{type}' is inactive.");

        var next = new SqlParameter("@next", SqlDbType.BigInt) { Direction = ParameterDirection.Output };
        await db.Database.ExecuteSqlRawAsync(NextValueSql,
            [
                new SqlParameter("@company_id", SqlDbType.BigInt) { Value = companyId },
                new SqlParameter("@rule_id", SqlDbType.BigInt) { Value = rule.Id },
                new SqlParameter("@period_key", SqlDbType.VarChar, 8) { Value = rule.PeriodKey(documentDate) },
                new SqlParameter("@now", SqlDbType.DateTime2) { Scale = 6, Value = clock.LocalNow },
                new SqlParameter("@user_id", SqlDbType.BigInt) { Value = user.UserId ?? MyERP.Shared.Security.SystemUser.Id },
                next,
            ],
            cancellationToken);
        return rule.Format(documentDate, (long)next.Value);
    }
}
