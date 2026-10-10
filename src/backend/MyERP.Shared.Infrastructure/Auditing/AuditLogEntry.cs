using Microsoft.EntityFrameworkCore;
using MyERP.Shared.Auditing;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Shared.Infrastructure.Auditing;

/// <summary>
/// 稽核日誌（sys_audit_log，REQ_DS43.94）：只增不改、長期保留。資料變更由 <see cref="AuditTrailInterceptor"/> 與業務資料同一交易寫入；
/// 安全事件由 <see cref="IAuditTrail.AddSecurityEvent"/> 寫入。不實作 <c>ICompanyOwned</c>：安全事件可沒有公司，查詢端自行篩選公司。
/// </summary>
[NotAudited]
public sealed class AuditLogEntry
{
    public long Id { get; set; }

    /// <summary>資料所屬公司（業務表取其 company_id，全系統表取操作時的公司）；登入等事件可為 null。</summary>
    public long? CompanyId { get; set; }

    /// <summary>操作者；背景作業為 0（與 created_by 相同），未登入的安全事件可為 null。</summary>
    public long? UserId { get; set; }

    /// <summary>安全事件輸入或確認的帳號（登入失敗時帳號可能不存在）；資料變更為 null，顯示時取 sys_user。</summary>
    public string? Username { get; set; }

    public DateTime OccurredAt { get; set; }

    public required string Category { get; set; }

    public required string Action { get; set; }

    /// <summary>資料表名稱（資料變更）；安全事件為 null。</summary>
    public string? TableName { get; set; }

    /// <summary>主鍵值（複合鍵以逗號分隔）。</summary>
    public string? RecordId { get; set; }

    /// <summary>JSON：資料變更為 <c>{"欄位":{"old":…,"new":…}}</c>；安全事件為事件明細。</summary>
    public string? Changes { get; set; }

    public string? RemoteIp { get; set; }

    public string? TraceId { get; set; }
}

/// <summary>稽核表的 EF 模型；由 <c>AddMyErpPersistence</c> 註冊，所有主機共用。</summary>
public sealed class AuditLogDbModel : IModuleDbModel
{
    public const string TableName = "sys_audit_log";

    public const string AppendOnlyTrigger = "trg_sys_audit_log_append_only";

    public void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.Entity<AuditLogEntry>(e =>
        {
            e.ToTable(TableName, table =>
            {
                table.HasCheckConstraint("ck_sys_audit_log_changes_json", "[changes] IS NULL OR ISJSON([changes]) = 1");
                // 只增不改觸發程序（System_AuditLog migration）；宣告後 EF 不以 OUTPUT 子句取回 Id
                table.HasTrigger(AppendOnlyTrigger);
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Username).HasMaxLength(100);
            e.Property(x => x.Category).HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.Action).HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.TableName).HasMaxLength(128).IsUnicode(false);
            e.Property(x => x.RecordId).HasMaxLength(100).IsUnicode(false);
            e.Property(x => x.Changes).HasColumnType("nvarchar(max)");
            e.Property(x => x.RemoteIp).HasMaxLength(45).IsUnicode(false);
            e.Property(x => x.TraceId).HasMaxLength(64).IsUnicode(false);
            e.HasIndex(x => new { x.CompanyId, x.OccurredAt }).HasDatabaseName("ix_sys_audit_log_company_id_occurred_at");
            e.HasIndex(x => new { x.UserId, x.OccurredAt }).HasDatabaseName("ix_sys_audit_log_user_id_occurred_at");
            e.HasIndex(x => new { x.TableName, x.RecordId, x.OccurredAt }).HasDatabaseName("ix_sys_audit_log_table_name_record_id_occurred_at");
        });
    }
}
