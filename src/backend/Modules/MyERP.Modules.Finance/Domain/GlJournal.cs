using MyERP.Shared.Domain;

namespace MyERP.Modules.Finance.Domain;

/// <summary>Permanent identity for a committed posting source. Rows are never soft deleted.</summary>
public sealed class GlSourceKey : BusinessEntity
{
    public string SourceModule { get; set; } = "";
    public string SourceType { get; set; } = "";
    public long SourceId { get; set; }
    public int SourceVersion { get; set; }
    public string Fingerprint { get; set; } = "";
}

/// <summary>Saved, immutable Posted journal header; business date is retained for period protection.</summary>
public sealed class GlJournal : BusinessEntity
{
    public long PeriodId { get; set; }
    public long SourceKeyId { get; set; }
    public DateOnly BusinessDate { get; set; }
    public string BaseCurrency { get; set; } = "";
    public GlJournalStatus Status { get; set; } = GlJournalStatus.Posted;
    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }
}

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<GlJournalStatus>))]
public enum GlJournalStatus { Posted }

/// <summary>
/// Saved base-currency journal leg. Account identity is a scalar MD reference with a permanent
/// name/version and FI mapping-version snapshot; later MD changes never rewrite it (BR-FI-027).
/// </summary>
public sealed class GlJournalLine : BusinessEntity
{
    public long JournalId { get; set; }
    public int Sequence { get; set; }
    public long AccountId { get; set; }
    public string AccountCode { get; set; } = "";
    public string AccountName { get; set; } = "";
    public int AccountVersion { get; set; }
    public int MappingVersion { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
}
