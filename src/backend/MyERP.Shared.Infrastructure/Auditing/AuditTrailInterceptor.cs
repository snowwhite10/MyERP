using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using MyERP.Shared.Auditing;
using MyERP.Shared.Domain;
using MyERP.Shared.Security;
using MyERP.Shared.Time;

namespace MyERP.Shared.Infrastructure.Auditing;

/// <summary>
/// <see cref="IAuditTrail"/> 的 scoped 狀態：業務操作標記與待寫入的安全事件，由 <see cref="AuditTrailInterceptor"/> 在 SaveChanges 時取用。
/// </summary>
public sealed class AuditTrail : IAuditTrail
{
    private readonly Dictionary<object, string> _actions = new(ReferenceEqualityComparer.Instance);
    private readonly List<SecurityAuditEvent> _securityEvents = [];

    public void MarkAction(object entity, string action)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (!AuditActions.BusinessActions.Contains(action))
        {
            throw new ArgumentOutOfRangeException(nameof(action), action, "Only VOID, APPROVE, REJECT and POST can be marked explicitly.");
        }

        _actions[entity] = action;
    }

    public void AddSecurityEvent(SecurityAuditEvent auditEvent)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        ArgumentException.ThrowIfNullOrWhiteSpace(auditEvent.Action);
        _securityEvents.Add(auditEvent);
    }

    internal string? TakeAction(object entity) => _actions.Remove(entity, out var action) ? action : null;

    internal IReadOnlyList<SecurityAuditEvent> TakeSecurityEvents()
    {
        var events = _securityEvents.ToArray();
        _securityEvents.Clear();
        return events;
    }
}

/// <summary>
/// 稽核 interceptor（MASTER_PROMPT 8.2、ADR-019）：把新增／修改／刪除寫入 sys_audit_log，與業務資料在同一交易。
/// 修改與刪除在 SaveChanges 前加入；新增要等資料庫產生 Id，於 SaveChanges 後再寫一次——有稽核記錄而呼叫端沒有開交易時由本 interceptor 開交易，
/// 業務資料與稽核一起提交，任何一步失敗即整體回滾。須排在 <see cref="Persistence.AuditSaveChangesInterceptor"/> 之後。
/// 不經 ChangeTracker 的 <c>ExecuteUpdate</c>／<c>ExecuteDelete</c> 不會被記錄。
/// </summary>
public sealed class AuditTrailInterceptor(
    AuditTrail trail,
    IClock clock,
    ICurrentUser currentUser,
    ICurrentCompany currentCompany,
    IHttpContextAccessor httpContextAccessor) : SaveChangesInterceptor
{
    private const string Masked = "***";

    private static readonly HashSet<string> ExcludedProperties = new(StringComparer.Ordinal)
    {
        nameof(IAuditable.CreatedAt),
        nameof(IAuditable.CreatedBy),
        nameof(IAuditable.UpdatedAt),
        nameof(IAuditable.UpdatedBy),
        nameof(IVersioned.Version),
        nameof(ICompanyOwned.CompanyId),
    };

    private readonly List<EntityEntry> _pendingAdded = [];
    private readonly List<AuditLogEntry> _staged = [];
    private IDbContextTransaction? _ownedTransaction;
    private bool _flushing;

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (Collect(eventData?.Context) is { } context)
        {
            _ownedTransaction = context.Database.BeginTransaction();
        }

        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (Collect(eventData?.Context) is { } context)
        {
            _ownedTransaction = await context.Database.BeginTransactionAsync(cancellationToken);
        }

        return result;
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        var context = eventData?.Context;
        if (context is null || _flushing)
        {
            return result;
        }

        _staged.Clear();
        var entries = TakeAddedEntries();
        _flushing = true;
        try
        {
            if (entries.Count > 0)
            {
                context.AddRange(entries);
                context.SaveChanges();
            }

            _ownedTransaction?.Commit();
        }
        catch
        {
            Detach(context, entries);
            _ownedTransaction?.Rollback();
            throw;
        }
        finally
        {
            _flushing = false;
            DisposeTransaction();
        }

        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        var context = eventData?.Context;
        if (context is null || _flushing)
        {
            return result;
        }

        _staged.Clear();
        var entries = TakeAddedEntries();
        _flushing = true;
        try
        {
            if (entries.Count > 0)
            {
                context.AddRange(entries);
                await context.SaveChangesAsync(cancellationToken);
            }

            if (_ownedTransaction is not null)
            {
                await _ownedTransaction.CommitAsync(cancellationToken);
            }
        }
        catch
        {
            Detach(context, entries);
            if (_ownedTransaction is not null)
            {
                await _ownedTransaction.RollbackAsync(CancellationToken.None);
            }

            throw;
        }
        finally
        {
            _flushing = false;
            DisposeTransaction();
        }

        return result;
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        if (Reset(eventData?.Context))
        {
            _ownedTransaction?.Rollback();
            DisposeTransaction();
        }
    }

    public override async Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        if (Reset(eventData?.Context) && _ownedTransaction is not null)
        {
            await _ownedTransaction.RollbackAsync(CancellationToken.None);
            DisposeTransaction();
        }
    }

    /// <summary>樂觀鎖衝突：EF 不呼叫 SaveChangesFailed，須在此清除本次稽核記錄並回滾自開的交易。</summary>
    public override InterceptionResult ThrowingConcurrencyException(ConcurrencyExceptionEventData eventData, InterceptionResult result)
    {
        SaveChangesFailed(eventData);
        return result;
    }

    public override async ValueTask<InterceptionResult> ThrowingConcurrencyExceptionAsync(
        ConcurrencyExceptionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
    {
        await SaveChangesFailedAsync(eventData, cancellationToken);
        return result;
    }

    /// <summary>收集本次變更；需要由本 interceptor 開交易時回傳 context。</summary>
    private DbContext? Collect(DbContext? context)
    {
        if (context is null || _flushing || context.Model.FindEntityType(typeof(AuditLogEntry)) is null)
        {
            return null;
        }

        context.ChangeTracker.DetectChanges();
        foreach (var entry in context.ChangeTracker.Entries().ToList())
        {
            if (!IsAudited(entry))
            {
                continue;
            }

            switch (entry.State)
            {
                case EntityState.Added:
                    _pendingAdded.Add(entry);
                    break;
                case EntityState.Modified:
                    if (BuildModified(entry) is { } modified)
                    {
                        _staged.Add(modified);
                    }

                    break;
                case EntityState.Deleted:
                    _staged.Add(BuildDataEntry(entry, AuditActions.Delete, WriteChanges(entry, old: true, current: false)));
                    break;
                default:
                    break;
            }
        }

        foreach (var auditEvent in trail.TakeSecurityEvents())
        {
            _staged.Add(BuildSecurityEntry(auditEvent));
        }

        context.AddRange(_staged);

        // EF 對單一批次不開交易：稽核 INSERT 可能在業務 UPDATE 失敗（例如版本衝突）後仍被提交，故有稽核記錄就自行開交易
        var hasAuditEntries = _pendingAdded.Count > 0 || _staged.Count > 0;
        return hasAuditEntries && context.Database.CurrentTransaction is null ? context : null;
    }

    private bool Reset(DbContext? context)
    {
        if (context is null || _flushing)
        {
            return false;
        }

        // 失敗後移除本次加入的稽核記錄，避免呼叫端重試 SaveChanges 時重複寫入
        Detach(context, _staged);
        _staged.Clear();
        _pendingAdded.Clear();
        return true;
    }

    private List<AuditLogEntry> TakeAddedEntries()
    {
        var entries = _pendingAdded.Select(e => BuildDataEntry(e, AuditActions.Create, WriteChanges(e, old: false, current: true))).ToList();
        _pendingAdded.Clear();
        return entries;
    }

    private static void Detach(DbContext context, IEnumerable<AuditLogEntry> entries)
    {
        foreach (var entry in entries)
        {
            context.Entry(entry).State = EntityState.Detached;
        }
    }

    private void DisposeTransaction()
    {
        _ownedTransaction?.Dispose();
        _ownedTransaction = null;
    }

    private static bool IsAudited(EntityEntry entry) =>
        entry.Metadata.FindPrimaryKey() is not null
        && !entry.Metadata.IsOwned()
        && entry.Metadata.ClrType.GetCustomAttribute<NotAuditedAttribute>() is null;

    private AuditLogEntry? BuildModified(EntityEntry entry)
    {
        var marked = trail.TakeAction(entry.Entity);
        var softDeleted = entry.Entity is ISoftDeletable
            && entry.Property(nameof(ISoftDeletable.IsDeleted)) is { IsModified: true, OriginalValue: false, CurrentValue: true };
        var changes = WriteChanges(entry, old: true, current: true);
        if (changes is null && marked is null)
        {
            return null;
        }

        return BuildDataEntry(entry, marked ?? (softDeleted ? AuditActions.Delete : AuditActions.Update), changes ?? "{}");
    }

    private AuditLogEntry BuildDataEntry(EntityEntry entry, string action, string? changes) => new()
    {
        CompanyId = entry.Entity is ICompanyOwned owned ? owned.CompanyId : CompanyForUnownedData,
        UserId = currentUser.UserId ?? SystemUser.Id,
        OccurredAt = clock.LocalNow,
        Category = AuditCategories.Data,
        Action = action,
        TableName = entry.Metadata.GetTableName(),
        RecordId = KeyOf(entry),
        Changes = changes,
        RemoteIp = RemoteIp,
        TraceId = TraceId,
    };

    private AuditLogEntry BuildSecurityEntry(SecurityAuditEvent auditEvent) => new()
    {
        CompanyId = auditEvent.CompanyId ?? CompanyForUnownedData,
        UserId = auditEvent.UserId,
        Username = auditEvent.Username is { Length: > 100 } username ? username[..100] : auditEvent.Username,
        OccurredAt = clock.LocalNow,
        Category = AuditCategories.Security,
        Action = auditEvent.Action,
        Changes = auditEvent.Detail is { Count: > 0 } detail ? WriteDetail(detail) : null,
        RemoteIp = RemoteIp,
        TraceId = TraceId,
    };

    /// <summary>全系統表（例如 sys_user、sys_role）的變更歸入操作時的公司；未登入請求的公司標頭未經驗證，不採用。</summary>
    private long? CompanyForUnownedData => currentUser.IsAuthenticated ? currentCompany.CompanyId : null;

    private string? RemoteIp
    {
        get
        {
            var address = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress;
            return (address is { IsIPv4MappedToIPv6: true } ? address.MapToIPv4() : address)?.ToString();
        }
    }

    private string? TraceId => Activity.Current?.TraceId.ToHexString() ?? httpContextAccessor.HttpContext?.TraceIdentifier;

    private static string KeyOf(EntityEntry entry) =>
        string.Join(',', entry.Metadata.FindPrimaryKey()!.Properties
            .Select(p => Convert.ToString(entry.Property(p.Name).CurrentValue, CultureInfo.InvariantCulture)));

    /// <summary>以欄位名稱（snake_case）寫出 <c>{"欄位":{"old":…,"new":…}}</c>；修改時只列值有變的欄位，沒有任何欄位時回傳 null。</summary>
    private static string? WriteChanges(EntityEntry entry, bool old, bool current)
    {
        var table = StoreObjectIdentifier.Create(entry.Metadata, StoreObjectType.Table);
        using var stream = new MemoryStream();
        var count = 0;
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in entry.Properties)
            {
                var metadata = property.Metadata;
                if (metadata.IsPrimaryKey() || ExcludedProperties.Contains(metadata.Name))
                {
                    continue;
                }

                var oldValue = property.OriginalValue;
                var newValue = property.CurrentValue;
                if (old && current && (!property.IsModified || Equals(oldValue, newValue)))
                {
                    continue;
                }

                if (!old && newValue is null)
                {
                    continue;
                }

                var sensitive = metadata.PropertyInfo?.GetCustomAttribute<SensitiveDataAttribute>() is not null;
                writer.WritePropertyName(table is { } t ? metadata.GetColumnName(t) ?? metadata.Name : metadata.Name);
                writer.WriteStartObject();
                if (old)
                {
                    writer.WritePropertyName("old");
                    WriteValue(writer, sensitive && oldValue is not null ? Masked : oldValue);
                }

                if (current)
                {
                    writer.WritePropertyName("new");
                    WriteValue(writer, sensitive && newValue is not null ? Masked : newValue);
                }

                writer.WriteEndObject();
                count++;
            }

            writer.WriteEndObject();
        }

        return count == 0 && old && current ? null : Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string WriteDetail(IReadOnlyDictionary<string, object?> detail)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var (key, value) in detail)
            {
                writer.WritePropertyName(key);
                WriteValue(writer, value);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case Enum e:
                writer.WriteStringValue(e.ToString());
                break;
            case DateTime d:
                writer.WriteStringValue(d.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFF", CultureInfo.InvariantCulture));
                break;
            case byte[] bytes:
                writer.WriteStringValue($"(binary {bytes.Length} bytes)");
                break;
            default:
                JsonSerializer.Serialize(writer, value, value.GetType());
                break;
        }
    }
}
