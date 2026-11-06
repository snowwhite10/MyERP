using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MyERP.Modules.Finance.Domain;

/// <summary>FI report a continuation token belongs to (BR-FI-127／130).</summary>
public enum FiReportKind
{
    ArDetail,
    ApDetail,
    OtherIncomeExpense
}

/// <summary>
/// What a continuation must repeat exactly (BR-FI-130). AR／AP detail uses <see cref="AsOf"/> and an optional
/// <see cref="PartnerId"/>; other income／expense uses inclusive <see cref="From"/>..<see cref="Through"/> and an
/// optional <see cref="KindFilter"/> (OtherCollection or OtherPayment). <see cref="ActorScopeDigest"/> is the caller's
/// 64-character lowercase hex SHA-256 summary of the actor and the company／partner scope it was granted, so a role
/// or scope change rejects an old token; it is not authority.
/// </summary>
public sealed record FiReportCursorBinding(
    long CompanyId,
    string? ActorScopeDigest,
    FiReportKind Kind,
    long? PartnerId,
    DateOnly? AsOf,
    DateOnly? From,
    DateOnly? Through,
    OtherIncomeExpenseKind? KindFilter,
    string? Currency,
    int PageSize);

/// <summary>Durable cutoff and manifest the first page was read under, and the next 1-based page (≥ 2).</summary>
public sealed record FiReportCursorPosition(long CutoffSequence, long ManifestId, int PageNumber);

/// <summary>
/// What a signed token may be used for (ADR-102). Each purpose has its own MAC domain, so a token signed for one fails
/// the signature check under the other. Continuation tokens point at page 2 or later; export tokens at page 1.
/// </summary>
public enum FiReportCursorPurpose
{
    Continuation,
    Export
}

public enum FiReportCursorError
{
    Required,
    OutOfRange,
    InvalidFormat,
    /// <summary>A filter that does not belong to the report kind.</summary>
    NotAllowed,
    Malformed,
    Tampered,
    Mismatch,
    Expired,
    KeyInvalid
}

/// <summary>
/// One error. <see cref="Collection"/> is <c>binding</c>, <c>position</c>, <c>clock</c>, <c>token</c>,
/// <c>manifest</c> or <c>key</c>.
/// </summary>
public sealed record FiReportCursorFieldError(string Collection, string Field, FiReportCursorError Error);

public enum FiReportCursorIssueStatus
{
    Issued,
    /// <summary>HTTP 400 (invalid binding or position).</summary>
    Rejected,
    /// <summary>HTTP 500: signing key missing or shorter than <see cref="FiReportCursorTokenRules.MinKeyLength"/>.</summary>
    Misconfigured
}

public sealed record FiReportCursorIssueResult(string? Token, IReadOnlyList<FiReportCursorFieldError> Errors)
{
    public FiReportCursorIssueStatus Status =>
        Errors.Any(e => e.Error == FiReportCursorError.KeyInvalid) ? FiReportCursorIssueStatus.Misconfigured
        : Errors.Count > 0 ? FiReportCursorIssueStatus.Rejected
        : FiReportCursorIssueStatus.Issued;
}

public enum FiReportCursorValidationStatus
{
    Valid,
    /// <summary>HTTP 400: missing, malformed, tampered or binding-mismatched token, or invalid request.</summary>
    Rejected,
    /// <summary>HTTP 409: the manifest is gone or expired; the caller must take a fresh cutoff explicitly.</summary>
    Expired,
    /// <summary>HTTP 500: signing key missing or too short.</summary>
    Misconfigured
}

public sealed record FiReportCursorValidationResult(FiReportCursorPosition? Position,
    IReadOnlyList<FiReportCursorFieldError> Errors)
{
    public FiReportCursorValidationStatus Status =>
        Errors.Any(e => e.Error == FiReportCursorError.KeyInvalid) ? FiReportCursorValidationStatus.Misconfigured
        : Errors.Any(e => e.Error == FiReportCursorError.Expired) ? FiReportCursorValidationStatus.Expired
        : Errors.Count > 0 ? FiReportCursorValidationStatus.Rejected
        : FiReportCursorValidationStatus.Valid;
}

/// <summary>
/// Pure FI report continuation token (BR-FI-130／131, FI-RPT-006, ADR-093, WU-BI-001a2). <see cref="Issue"/> signs
/// <c>base64url(payload) "." base64url(HMAC-SHA256(key, domain ‖ payload))</c>, where the canonical payload holds the
/// binding and position only — never permissions, so FI re-checks authority on every continuation.
/// <see cref="Validate"/> checks the key, the repeated request binding and the injected clock, then the token format,
/// the signature in constant time, a strictly canonical payload, every bound field, and last the manifest expiry
/// passed in by the caller (from the durable manifest, null when gone). It reads no clock, key store or database.
/// </summary>
public static class FiReportCursorTokenRules
{
    public const string MacDomain = "MyERP.FI.ReportCursor.v1\n";
    public const string ExportMacDomain = "MyERP.FI.ReportExport.v1\n";
    public const int MinKeyLength = 32;
    public const int MaxPageSize = 200;
    public const int MaxPageNumber = 1_000_000;
    public const int MaxTokenLength = 1024;

    private const string Version = "v1";
    private const int FieldCount = 14;
    private const int MacLength = 32;
    private const string DateFormat = "yyyy-MM-dd";

    public static FiReportCursorIssueResult Issue(FiReportCursorBinding? binding, FiReportCursorPosition? position,
        byte[]? key, FiReportCursorPurpose purpose = FiReportCursorPurpose.Continuation)
    {
        if (!Enum.IsDefined(purpose))
        {
            return new FiReportCursorIssueResult(null, [PurposeError]);
        }

        if (!KeyValid(key))
        {
            return new FiReportCursorIssueResult(null, [KeyError]);
        }

        var errors = new List<FiReportCursorFieldError>();
        ValidateBinding(binding, errors);
        ValidatePosition(position, purpose, errors);
        if (errors.Count > 0)
        {
            return new FiReportCursorIssueResult(null, errors);
        }

        var payload = Encoding.UTF8.GetBytes(Serialize(binding!, position!));
        return new FiReportCursorIssueResult(
            Base64Url.EncodeToString(payload) + "." + Base64Url.EncodeToString(Sign(key!, payload, purpose)), []);
    }

    public static FiReportCursorValidationResult Validate(string? token, FiReportCursorBinding? binding, byte[]? key,
        DateTimeOffset now, DateTimeOffset? manifestExpiresAt,
        FiReportCursorPurpose purpose = FiReportCursorPurpose.Continuation)
    {
        if (!Enum.IsDefined(purpose))
        {
            return Fail(PurposeError);
        }

        if (!KeyValid(key))
        {
            return Fail(KeyError);
        }

        var errors = new List<FiReportCursorFieldError>();
        ValidateBinding(binding, errors);
        if (now == default)
        {
            errors.Add(new FiReportCursorFieldError("clock", "now", FiReportCursorError.Required));
        }

        if (errors.Count > 0)
        {
            return new FiReportCursorValidationResult(null, errors);
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return Fail(new FiReportCursorFieldError("token", "token", FiReportCursorError.Required));
        }

        if (!TrySplit(token, out var payload, out var mac))
        {
            return Fail(new FiReportCursorFieldError("token", "token", FiReportCursorError.Malformed));
        }

        if (!CryptographicOperations.FixedTimeEquals(Sign(key!, payload, purpose), mac))
        {
            return Fail(new FiReportCursorFieldError("token", "signature", FiReportCursorError.Tampered));
        }

        if (!TryParse(payload, purpose, out var bound, out var position))
        {
            return Fail(new FiReportCursorFieldError("token", "payload", FiReportCursorError.Malformed));
        }

        CompareBinding(bound!, binding!, errors);
        if (errors.Count > 0)
        {
            return new FiReportCursorValidationResult(null, errors);
        }

        if (manifestExpiresAt is not { } expiresAt || now >= expiresAt)
        {
            return Fail(new FiReportCursorFieldError("manifest", "expiresAt", FiReportCursorError.Expired));
        }

        return new FiReportCursorValidationResult(position, []);
    }

    private static readonly FiReportCursorFieldError KeyError = new("key", "key", FiReportCursorError.KeyInvalid);

    /// <summary>An undefined purpose is a server wiring fault (500), reported like a misconfigured key.</summary>
    private static readonly FiReportCursorFieldError PurposeError =
        new("key", "purpose", FiReportCursorError.KeyInvalid);

    private static FiReportCursorValidationResult Fail(FiReportCursorFieldError error) => new(null, [error]);

    private static bool KeyValid(byte[]? key) => key is { Length: >= MinKeyLength };

    private static byte[] Sign(byte[] key, byte[] payload, FiReportCursorPurpose purpose)
    {
        var domain = Encoding.UTF8.GetBytes(purpose == FiReportCursorPurpose.Export ? ExportMacDomain : MacDomain);
        var message = new byte[domain.Length + payload.Length];
        domain.CopyTo(message, 0);
        payload.CopyTo(message, domain.Length);
        return HMACSHA256.HashData(key, message);
    }

    /// <summary>Shared with <see cref="FiReportManifestRules"/> so a manifest binds exactly what a token may.</summary>
    internal static void ValidateBinding(FiReportCursorBinding? b, List<FiReportCursorFieldError> errors)
    {
        void Add(string field, FiReportCursorError error) =>
            errors.Add(new FiReportCursorFieldError("binding", field, error));

        if (b is null)
        {
            Add("binding", FiReportCursorError.Required);
            return;
        }

        if (b.CompanyId <= 0)
        {
            Add("companyId", FiReportCursorError.OutOfRange);
        }

        if (string.IsNullOrEmpty(b.ActorScopeDigest))
        {
            Add("actorScopeDigest", FiReportCursorError.Required);
        }
        else if (b.ActorScopeDigest.Length != 64 || !b.ActorScopeDigest.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
        {
            Add("actorScopeDigest", FiReportCursorError.InvalidFormat);
        }

        if (!Enum.IsDefined(b.Kind))
        {
            Add("kind", FiReportCursorError.OutOfRange);
        }
        else if (b.Kind == FiReportKind.OtherIncomeExpense)
        {
            if (b.PartnerId is not null)
            {
                Add("partnerId", FiReportCursorError.NotAllowed);
            }

            if (b.AsOf is not null)
            {
                Add("asOf", FiReportCursorError.NotAllowed);
            }

            var fromOk = RequireDate(b.From, "from", Add);
            if (RequireDate(b.Through, "through", Add) && fromOk && b.Through < b.From)
            {
                Add("through", FiReportCursorError.OutOfRange);
            }

            if (b.KindFilter is { } kind && kind is not (OtherIncomeExpenseKind.OtherCollection
                    or OtherIncomeExpenseKind.OtherPayment))
            {
                Add("kindFilter", FiReportCursorError.OutOfRange);
            }
        }
        else
        {
            if (b.PartnerId is <= 0)
            {
                Add("partnerId", FiReportCursorError.OutOfRange);
            }

            RequireDate(b.AsOf, "asOf", Add);
            if (b.From is not null)
            {
                Add("from", FiReportCursorError.NotAllowed);
            }

            if (b.Through is not null)
            {
                Add("through", FiReportCursorError.NotAllowed);
            }

            if (b.KindFilter is not null)
            {
                Add("kindFilter", FiReportCursorError.NotAllowed);
            }
        }

        if (string.IsNullOrEmpty(b.Currency))
        {
            Add("currency", FiReportCursorError.Required);
        }
        else if (b.Currency.Length != 3 || !b.Currency.All(c => c is >= 'A' and <= 'Z'))
        {
            Add("currency", FiReportCursorError.InvalidFormat);
        }

        if (b.PageSize is < 1 or > MaxPageSize)
        {
            Add("pageSize", FiReportCursorError.OutOfRange);
        }
    }

    private static bool RequireDate(DateOnly? date, string field, Action<string, FiReportCursorError> add)
    {
        if (date is null || date == default(DateOnly))
        {
            add(field, FiReportCursorError.Required);
            return false;
        }

        return true;
    }

    private static void ValidatePosition(FiReportCursorPosition? p, FiReportCursorPurpose purpose,
        List<FiReportCursorFieldError> errors)
    {
        void Add(string field, FiReportCursorError error) =>
            errors.Add(new FiReportCursorFieldError("position", field, error));

        if (p is null)
        {
            Add("position", FiReportCursorError.Required);
            return;
        }

        if (p.CutoffSequence <= 0)
        {
            Add("cutoffSequence", FiReportCursorError.OutOfRange);
        }

        if (p.ManifestId <= 0)
        {
            Add("manifestId", FiReportCursorError.OutOfRange);
        }

        if (purpose == FiReportCursorPurpose.Export ? p.PageNumber != 1 : p.PageNumber is < 2 or > MaxPageNumber)
        {
            Add("pageNumber", FiReportCursorError.OutOfRange);
        }
    }

    private static void CompareBinding(FiReportCursorBinding token, FiReportCursorBinding request,
        List<FiReportCursorFieldError> errors)
    {
        void Check(bool same, string field)
        {
            if (!same)
            {
                errors.Add(new FiReportCursorFieldError("binding", field, FiReportCursorError.Mismatch));
            }
        }

        Check(token.CompanyId == request.CompanyId, "companyId");
        Check(string.Equals(token.ActorScopeDigest, request.ActorScopeDigest, StringComparison.Ordinal),
            "actorScopeDigest");
        Check(token.Kind == request.Kind, "kind");
        Check(token.PartnerId == request.PartnerId, "partnerId");
        Check(token.AsOf == request.AsOf, "asOf");
        Check(token.From == request.From, "from");
        Check(token.Through == request.Through, "through");
        Check(token.KindFilter == request.KindFilter, "kindFilter");
        Check(string.Equals(token.Currency, request.Currency, StringComparison.Ordinal), "currency");
        Check(token.PageSize == request.PageSize, "pageSize");
    }

    private static string Serialize(FiReportCursorBinding b, FiReportCursorPosition p) => string.Join('|',
        Version,
        Number(b.CompanyId),
        b.ActorScopeDigest,
        b.Kind.ToString(),
        b.PartnerId is { } partner ? Number(partner) : "",
        Date(b.AsOf),
        Date(b.From),
        Date(b.Through),
        b.KindFilter?.ToString() ?? "",
        b.Currency,
        Number(b.PageSize),
        Number(p.CutoffSequence),
        Number(p.ManifestId),
        Number(p.PageNumber));

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Date(DateOnly? value) =>
        value?.ToString(DateFormat, CultureInfo.InvariantCulture) ?? "";

    private static bool TrySplit(string token, out byte[] payload, out byte[] mac)
    {
        payload = [];
        mac = [];
        if (token.Length > MaxTokenLength)
        {
            return false;
        }

        var parts = token.Split('.');
        return parts.Length == 2 && TryDecode(parts[0], out payload) && TryDecode(parts[1], out mac);
    }

    private static bool TryDecode(string text, out byte[] bytes)
    {
        bytes = [];
        if (text.Length == 0 || text.Length % 4 == 1
            || !text.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_'))
        {
            return false;
        }

        try
        {
            bytes = Base64Url.DecodeFromChars(text);
        }
        catch (FormatException)
        {
            return false;
        }

        // Only the canonical encoding is accepted (unused trailing bits must be zero).
        return string.Equals(Base64Url.EncodeToString(bytes), text, StringComparison.Ordinal);
    }

    private static bool TryParse(byte[] payload, FiReportCursorPurpose purpose, out FiReportCursorBinding? binding,
        out FiReportCursorPosition? position)
    {
        binding = null;
        position = null;
        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(payload);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        var f = text.Split('|');
        if (f.Length != FieldCount || f[0] != Version
            || !long.TryParse(f[1], NumberStyles.None, CultureInfo.InvariantCulture, out var company)
            || !Enum.TryParse<FiReportKind>(f[3], false, out var kind)
            || !OptionalLong(f[4], out var partner)
            || !OptionalDate(f[5], out var asOf)
            || !OptionalDate(f[6], out var from)
            || !OptionalDate(f[7], out var through)
            || !OptionalOtherKind(f[8], out var kindFilter)
            || !int.TryParse(f[10], NumberStyles.None, CultureInfo.InvariantCulture, out var pageSize)
            || !long.TryParse(f[11], NumberStyles.None, CultureInfo.InvariantCulture, out var cutoff)
            || !long.TryParse(f[12], NumberStyles.None, CultureInfo.InvariantCulture, out var manifest)
            || !int.TryParse(f[13], NumberStyles.None, CultureInfo.InvariantCulture, out var page))
        {
            return false;
        }

        var b = new FiReportCursorBinding(company, f[2], kind, partner, asOf, from, through, kindFilter, f[9],
            pageSize);
        var p = new FiReportCursorPosition(cutoff, manifest, page);
        var errors = new List<FiReportCursorFieldError>();
        ValidateBinding(b, errors);
        ValidatePosition(p, purpose, errors);
        if (errors.Count > 0 || !string.Equals(Serialize(b, p), text, StringComparison.Ordinal))
        {
            return false;
        }

        binding = b;
        position = p;
        return true;
    }

    private static bool OptionalLong(string text, out long? value)
    {
        value = null;
        if (text.Length == 0)
        {
            return true;
        }

        if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool OptionalDate(string text, out DateOnly? value)
    {
        value = null;
        if (text.Length == 0)
        {
            return true;
        }

        if (!DateOnly.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None,
                out var parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool OptionalOtherKind(string text, out OtherIncomeExpenseKind? value)
    {
        value = null;
        if (text.Length == 0)
        {
            return true;
        }

        if (!Enum.TryParse<OtherIncomeExpenseKind>(text, false, out var parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }
}
