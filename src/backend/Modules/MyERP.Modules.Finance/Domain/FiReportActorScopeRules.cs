using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MyERP.Modules.Finance.Domain;

/// <summary>
/// Partner scope the actor was granted for an FI report: every partner of the company (<see cref="All"/>) or an
/// explicit, non-empty set of partner IDs. Use <see cref="All"/> or <see cref="Partners"/>.
/// </summary>
public sealed record FiReportPartnerScope(bool IsAll, IReadOnlyCollection<long>? PartnerIds)
{
    public static readonly FiReportPartnerScope All = new(true, null);

    public static FiReportPartnerScope Partners(IReadOnlyCollection<long> partnerIds) => new(false, partnerIds);
}

public enum FiReportActorScopeError
{
    Required,
    OutOfRange,
    /// <summary>A partner list given together with the all-partners scope.</summary>
    NotAllowed
}

/// <summary>One error; <see cref="Field"/> is a camelCase input name.</summary>
public sealed record FiReportActorScopeFieldError(string Field, FiReportActorScopeError Error);

public sealed record FiReportActorScopeResult(string? Digest, IReadOnlyList<FiReportActorScopeFieldError> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Pure FI report actor scope digest (BR-FI-130／131, ADR-097, WU-BI-001a7) for
/// <see cref="FiReportCursorBinding.ActorScopeDigest"/>. The caller resolves the actor's authority (from SYS through
/// its contract) and passes the actor, company, report kind, the role IDs that grant the report and the granted
/// partner scope. The canonical text sorts and de-duplicates role and partner IDs, writes invariant numbers and a
/// version prefix; the digest is lowercase hex SHA-256 over <see cref="HashDomain"/> followed by that text. Any
/// added／removed partner, all↔partial switch, revoked or added granting role, or another actor／company／kind
/// changes the digest, so an old continuation token fails with a binding mismatch. The digest is not authority:
/// FI still checks authority on every read. It reads no clock, permission store or database.
/// </summary>
public static class FiReportActorScopeRules
{
    public const string HashDomain = "MyERP.FI.ReportActorScope.v1\n";
    public const int MaxRoles = 1_000;
    public const int MaxPartners = 100_000;

    private const string Version = "v1";

    public static FiReportActorScopeResult Digest(long actorUserId, long companyId, FiReportKind kind,
        IReadOnlyCollection<long>? roleIds, FiReportPartnerScope? partnerScope)
    {
        var errors = Validate(actorUserId, companyId, kind, roleIds, partnerScope);
        if (errors.Count > 0)
        {
            return new FiReportActorScopeResult(null, errors);
        }

        var text = Build(actorUserId, companyId, kind, roleIds!, partnerScope!);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(HashDomain + text));
        return new FiReportActorScopeResult(Convert.ToHexStringLower(hash), []);
    }

    /// <summary>Canonical text that is hashed (for diagnostics and tests); throws on invalid input.</summary>
    public static string Canonical(long actorUserId, long companyId, FiReportKind kind,
        IReadOnlyCollection<long>? roleIds, FiReportPartnerScope? partnerScope)
    {
        var errors = Validate(actorUserId, companyId, kind, roleIds, partnerScope);
        if (errors.Count > 0)
        {
            throw new ArgumentException("Invalid actor scope: "
                + string.Join(", ", errors.Select(e => e.Field + ":" + e.Error)));
        }

        return Build(actorUserId, companyId, kind, roleIds!, partnerScope!);
    }

    private static List<FiReportActorScopeFieldError> Validate(long actorUserId, long companyId, FiReportKind kind,
        IReadOnlyCollection<long>? roleIds, FiReportPartnerScope? partnerScope)
    {
        var errors = new List<FiReportActorScopeFieldError>();
        void Add(string field, FiReportActorScopeError error) => errors.Add(new FiReportActorScopeFieldError(field, error));

        if (actorUserId <= 0)
        {
            Add("actorUserId", FiReportActorScopeError.OutOfRange);
        }

        if (companyId <= 0)
        {
            Add("companyId", FiReportActorScopeError.OutOfRange);
        }

        if (!Enum.IsDefined(kind))
        {
            Add("kind", FiReportActorScopeError.OutOfRange);
        }

        IdSetError(roleIds, MaxRoles, "roleIds", Add);

        if (partnerScope is null)
        {
            Add("partnerScope", FiReportActorScopeError.Required);
        }
        else if (partnerScope.IsAll)
        {
            if (partnerScope.PartnerIds is not null)
            {
                Add("partnerIds", FiReportActorScopeError.NotAllowed);
            }
        }
        else
        {
            IdSetError(partnerScope.PartnerIds, MaxPartners, "partnerIds", Add);
        }

        return errors;
    }

    private static void IdSetError(IReadOnlyCollection<long>? ids, int max, string field,
        Action<string, FiReportActorScopeError> add)
    {
        if (ids is null || ids.Count == 0)
        {
            add(field, FiReportActorScopeError.Required);
        }
        else if (ids.Any(id => id <= 0) || ids.Distinct().Count() > max)
        {
            add(field, FiReportActorScopeError.OutOfRange);
        }
    }

    private static string Build(long actorUserId, long companyId, FiReportKind kind, IReadOnlyCollection<long> roleIds,
        FiReportPartnerScope partnerScope) => string.Join('|',
        Version,
        "actor=" + Number(actorUserId),
        "company=" + Number(companyId),
        "kind=" + kind,
        "roles=" + Ids(roleIds),
        "partners=" + (partnerScope.IsAll ? "*" : Ids(partnerScope.PartnerIds!)));

    private static string Ids(IReadOnlyCollection<long> ids) =>
        string.Join(',', ids.Distinct().Order().Select(Number));

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
}
