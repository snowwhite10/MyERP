using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.System.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;
using MyERP.Shared.Time;

namespace MyERP.Modules.System.Application.Numbering;

/// <summary>建立單號規則。</summary>
public sealed record NumberRuleInput(
    string DocumentType, string Name, string? Prefix, NumberDateFormat DateFormat, int SequenceLength, NumberResetPeriod ResetPeriod);

/// <summary>修改單號規則，須帶讀取時的 <c>version</c>。單據類型不可修改。</summary>
public sealed record NumberRuleUpdate(
    string Name, string? Prefix, NumberDateFormat DateFormat, int SequenceLength, NumberResetPeriod ResetPeriod, bool IsActive, int Version);

/// <summary>
/// 單號規則。<c>Sample</c> 為今天第 1 號的樣式；<c>IsInUse</c> 表示已發出單號，此時日期格式與重設週期不可修改、流水號長度不可縮短。
/// </summary>
public sealed record NumberRuleDetail(
    long Id, string DocumentType, string Name, string Prefix, NumberDateFormat DateFormat, int SequenceLength,
    NumberResetPeriod ResetPeriod, bool IsActive, bool IsInUse, string Sample, int Version);

/// <summary>下一個單號預覽（不佔用流水號）。</summary>
public sealed record NumberPreview(string DocumentType, DateOnly DocumentDate, string NextNumber);

/// <summary>單號規則維護（REQ_F4118.28.1–4）。只處理目前公司的規則；呼叫端以權限代碼保護。</summary>
public sealed class NumberRuleService(MyErpDbContext db, IClock clock)
{
    public const string NotFound = "SYS.NUMBER_RULE.NOT_FOUND";
    public const string DuplicateType = "SYS.NUMBER_RULE.DUPLICATE_TYPE";
    public const string InUse = "SYS.NUMBER_RULE.IN_USE";

    public async Task<PagedResult<NumberRuleDetail>> ListAsync(PagedRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();
        await SystemQueries.ActiveCompanyIdAsync(db, cancellationToken);
        var fields = SortSpec.Parse(request.Sort, ["documentType", "name"], "documentType");
        if (fields.Count != 1)
            throw new RequestValidationException("sort", "Choose one number rule sort field.");

        var query = db.Set<NumberRule>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            query = query.Where(x => x.DocumentType.Contains(keyword) || x.Name.Contains(keyword));
        }

        query = query.OrderByKeyOrName(fields[0], x => x.DocumentType, x => x.Name);
        var count = await query.LongCountAsync(cancellationToken);
        var rules = await query.Skip(request.Skip).Take(request.PageSize).ToListAsync(cancellationToken);
        var ids = rules.Select(r => r.Id).ToArray();
        var used = (await db.Set<NumberSequence>().AsNoTracking()
            .Where(s => ids.Contains(s.RuleId)).Select(s => s.RuleId).Distinct().ToListAsync(cancellationToken)).ToHashSet();
        return PagedResult.Create(rules.Select(r => ToDetail(r, used.Contains(r.Id))).ToList(), request, count);
    }

    public async Task<NumberRuleDetail> GetAsync(long id, CancellationToken cancellationToken = default)
    {
        await SystemQueries.ActiveCompanyIdAsync(db, cancellationToken);
        var rule = await FindAsync(id, false, cancellationToken);
        return ToDetail(rule, await IsInUseAsync(id, cancellationToken));
    }

    public async Task<NumberRuleDetail> CreateAsync(NumberRuleInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var companyId = await SystemQueries.ActiveCompanyIdAsync(db, cancellationToken);
        var rule = new NumberRule
        {
            CompanyId = companyId,
            DocumentType = NumberRule.NormalizeDocumentType(input.DocumentType),
            Name = ValidateName(input.Name),
            Prefix = NumberRule.NormalizePrefix(input.Prefix),
            DateFormat = input.DateFormat,
            SequenceLength = input.SequenceLength,
            ResetPeriod = input.ResetPeriod,
        };
        NumberRule.ValidateSettings(rule.DateFormat, rule.SequenceLength, rule.ResetPeriod);
        if (await db.Set<NumberRule>().AnyAsync(x => x.DocumentType == rule.DocumentType, cancellationToken))
            throw new ConflictException(DuplicateType, "A number rule already exists for this document type.");

        db.Add(rule);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new ConflictException(DuplicateType, "A number rule already exists for this document type.");
        }

        return ToDetail(rule, false);
    }

    public async Task<NumberRuleDetail> UpdateAsync(long id, NumberRuleUpdate input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await SystemQueries.ActiveCompanyIdAsync(db, cancellationToken);
        var name = ValidateName(input.Name);
        var prefix = NumberRule.NormalizePrefix(input.Prefix);
        NumberRule.ValidateSettings(input.DateFormat, input.SequenceLength, input.ResetPeriod);
        var rule = await FindAsync(id, true, cancellationToken);
        if (input.Version < 0 || rule.Version != input.Version) throw new ConflictException();

        // 已發出單號：改日期格式或重設週期會令新週期的流水號與舊單號重複，縮短長度會令既有計數超出上限
        var inUse = await IsInUseAsync(id, cancellationToken);
        if (inUse && (input.DateFormat != rule.DateFormat || input.ResetPeriod != rule.ResetPeriod || input.SequenceLength < rule.SequenceLength))
            throw new BusinessRuleException(InUse, "Numbers have been issued: date format and reset period are locked and the sequence length cannot shrink.");

        rule.Name = name;
        rule.Prefix = prefix;
        rule.DateFormat = input.DateFormat;
        rule.SequenceLength = input.SequenceLength;
        rule.ResetPeriod = input.ResetPeriod;
        rule.IsActive = input.IsActive;
        db.Entry(rule).Property(x => x.Version).OriginalValue = input.Version;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException();
        }

        return ToDetail(rule, inUse);
    }

    /// <summary>預覽指定業務日期（預設今天）的下一個單號；只讀取計數，不佔用流水號，並行時實際取得的號碼可能較大。</summary>
    public async Task<NumberPreview> PreviewAsync(long id, DateOnly? documentDate, CancellationToken cancellationToken = default)
    {
        await SystemQueries.ActiveCompanyIdAsync(db, cancellationToken);
        var rule = await FindAsync(id, false, cancellationToken);
        var date = documentDate ?? clock.Today;
        var period = rule.PeriodKey(date);
        var current = await db.Set<NumberSequence>().AsNoTracking()
            .Where(s => s.RuleId == id && s.PeriodKey == period)
            .Select(s => (long?)s.CurrentValue)
            .SingleOrDefaultAsync(cancellationToken) ?? 0;
        return new NumberPreview(rule.DocumentType, date, rule.Format(date, current + 1));
    }

    private async Task<NumberRule> FindAsync(long id, bool tracking, CancellationToken token)
    {
        var query = tracking ? db.Set<NumberRule>() : db.Set<NumberRule>().AsNoTracking();
        return await query.SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new NotFoundException(NotFound, "Number rule", id);
    }

    private Task<bool> IsInUseAsync(long id, CancellationToken token) =>
        db.Set<NumberSequence>().AnyAsync(s => s.RuleId == id, token);


    private static string ValidateName(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > 100)
            throw new RequestValidationException("name", "Required; maximum 100 characters.");
        return trimmed;
    }

    private NumberRuleDetail ToDetail(NumberRule rule, bool inUse) => new(
        rule.Id, rule.DocumentType, rule.Name, rule.Prefix, rule.DateFormat, rule.SequenceLength, rule.ResetPeriod,
        rule.IsActive, inUse, rule.Format(clock.Today, 1), rule.Version);
}
