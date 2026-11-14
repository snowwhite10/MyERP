namespace MyERP.Shared.Infrastructure.Seeding;

/// <summary>示範單號規則的日期部分（WU-FI-005c3a、ADR-135）；由 SYS 對應到單號規則的日期格式。</summary>
public enum SeedNumberDatePart
{
    None,
    Year,
    YearMonth,
    YearMonthDay,
}

/// <summary>示範單號規則的流水號重設週期。</summary>
public enum SeedNumberReset
{
    Never,
    Yearly,
    Monthly,
    Daily,
}

/// <summary>
/// 一個業務模組需要的示範單號規則。SYS 種子程序在每間種子公司建立缺少的規則，業務模組不直接寫 SYS 資料表。
/// 例如 <c>AR.COLLECTION</c>、前綴 <c>RC</c>、年月、4 位、每月重設 → <c>RC2026110001</c>。
/// </summary>
public sealed record SeedNumberRuleDefinition(
    string DocumentType,
    string Name,
    string Prefix,
    SeedNumberDatePart DatePart = SeedNumberDatePart.YearMonth,
    int SequenceLength = 4,
    SeedNumberReset Reset = SeedNumberReset.Monthly);

/// <summary>
/// 業務模組以 singleton 註冊，宣告自己單據的示範單號規則（ADR-135）。SYS 驗證全部定義（格式、跨模組重複）後才寫入；
/// 只在 Development 種子執行，冪等，已存在（含已停用或已被用戶修改）的規則不變。
/// </summary>
public interface ISeedNumberRuleProvider
{
    /// <summary>宣告的模組代碼，用於錯誤訊息。</summary>
    string Module { get; }

    IReadOnlyList<SeedNumberRuleDefinition> Rules { get; }
}
