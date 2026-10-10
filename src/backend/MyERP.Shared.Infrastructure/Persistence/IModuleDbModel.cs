using Microsoft.EntityFrameworkCore;

namespace MyERP.Shared.Infrastructure.Persistence;

/// <summary>
/// 模組的 EF 模型（ADR-003）：每個模組實作一個，在 <c>IErpModule.AddServices</c> 以
/// <c>services.AddSingleton&lt;IModuleDbModel, XxxDbModel&gt;()</c> 註冊，於其中套用自己的 IEntityTypeConfiguration 並以模組前綴命名資料表。
/// 共用欄位（審計、公司、版本、軟刪除）的設定與篩選由 <see cref="MyErpDbContext"/> 統一套用，模組不必重複。
/// </summary>
public interface IModuleDbModel
{
    void Configure(ModelBuilder modelBuilder);
}
