namespace MyERP.Shared.Infrastructure.Persistence;

/// <summary>
/// 具名 Global Query Filter（EF Core 10）。需要含已刪資料時只略過軟刪除：
/// <c>IgnoreQueryFilters([ErpQueryFilters.SoftDelete])</c>，公司隔離仍然有效。
/// 略過 <see cref="Company"/> 必須在程式註解說明原因（db-conventions.md）。
/// </summary>
public static class ErpQueryFilters
{
    public const string Company = "Company";
    public const string SoftDelete = "SoftDelete";
}
