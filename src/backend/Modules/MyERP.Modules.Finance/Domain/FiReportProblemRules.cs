using System.Collections.Frozen;

namespace MyERP.Modules.Finance.Domain;

/// <summary>
/// HTTP outcome of an FI report rule failure. <see cref="FieldErrors"/> is safe to return to the client (camelCase
/// field → error names); <see cref="InternalDetails"/> (<c>collection.field:Error</c>, in input order) is only for the
/// traceId log and must never be returned.
/// </summary>
public sealed record FiReportProblem(
    int Status,
    string ErrorCode,
    IReadOnlyDictionary<string, string[]> FieldErrors,
    IReadOnlyList<string> InternalDetails);

/// <summary>
/// Pure mapping of ADR-093 cursor token and ADR-094 manifest results to an HTTP problem (ADR-096, WU-BI-001a6), so
/// the WU-BI-001b endpoints only assemble. Returns null when there is no problem. Only filters the client sends
/// (<see cref="ClientFields"/>) can produce a 400 field error; everything the server wires itself (key, clock,
/// company, actor scope, report kind, position, manifest, re-read, headers, TTL) is a 500 with no field detail, as is
/// any Inconsistent finding. Precedence: server fault／Inconsistent 500 → client filter 400 → expired 409 → token 400
/// → too large 422.
/// </summary>
public static class FiReportProblemRules
{
    public const string TokenInvalid = "FI.REPORT.TOKEN_INVALID";
    public const string ManifestExpired = "FI.REPORT.MANIFEST_EXPIRED";
    public const string TooLarge = "FI.REPORT.TOO_LARGE";

    /// <summary>Same value as <c>CommonErrorCodes.ValidationFailed</c> (Domain does not reference MyERP.Shared errors).</summary>
    public const string ValidationFailed = "COMMON.REQUEST.VALIDATION_FAILED";

    /// <summary>Same value as <c>CommonErrorCodes.InternalError</c>.</summary>
    public const string InternalError = "COMMON.SERVER.INTERNAL_ERROR";

    /// <summary>Binding fields that come from the request query; any other binding field is server-derived.</summary>
    public static readonly IReadOnlySet<string> ClientFields = FrozenSet.Create(StringComparer.Ordinal,
        "partnerId", "asOf", "from", "through", "kindFilter", "currency", "pageSize");

    private const string Invalid = "Invalid";

    public static FiReportProblem? Map(FiReportCursorValidationResult? result) =>
        result is null ? MissingInput("validation") : MapCursor(result.Errors);

    public static FiReportProblem? Map(FiReportCursorIssueResult? result) =>
        result is null ? MissingInput("issue") : MapCursor(result.Errors);

    public static FiReportProblem? Map(IReadOnlyList<FiReportManifestFieldError>? errors)
    {
        if (errors is null)
        {
            return MissingInput("manifest");
        }

        if (errors.Count == 0)
        {
            return null;
        }

        if (errors.Any(e => e is null))
        {
            return Problem(500, InternalError, [], ["problem.errors:Null"]);
        }

        var details = errors.Select(e => Detail(e.Collection, e.Field, e.Error.ToString())).ToList();
        var client = new List<(string Field, string Error)>();
        var tooLarge = false;
        foreach (var e in errors)
        {
            if (e.Error == FiReportManifestError.TooLarge)
            {
                tooLarge = true;
            }
            else if (e.Error != FiReportManifestError.Inconsistent && IsClient(e.Collection, e.Field))
            {
                client.Add((e.Field, e.Error.ToString()));
            }
            else
            {
                return Problem(500, InternalError, [], details);
            }
        }

        if (client.Count > 0)
        {
            return Problem(400, ValidationFailed, client, details);
        }

        return tooLarge ? Problem(422, TooLarge, [], details) : Problem(500, InternalError, [], details);
    }

    private static FiReportProblem? MapCursor(IReadOnlyList<FiReportCursorFieldError>? errors)
    {
        if (errors is null)
        {
            return MissingInput("errors");
        }

        if (errors.Count == 0)
        {
            return null;
        }

        if (errors.Any(e => e is null))
        {
            return Problem(500, InternalError, [], ["problem.errors:Null"]);
        }

        var details = errors.Select(e => Detail(e.Collection, e.Field, e.Error.ToString())).ToList();
        var client = new List<(string Field, string Error)>();
        var expired = false;
        var tokenInvalid = false;
        foreach (var e in errors)
        {
            switch (e)
            {
                case { Error: FiReportCursorError.Expired, Collection: "manifest" }:
                    expired = true;
                    break;
                case { Error: FiReportCursorError.Mismatch, Collection: "binding" }:
                    tokenInvalid = true;
                    break;
                case { Collection: "token", Error: FiReportCursorError.Required }:
                    break; // reported as token Required below unless a stronger outcome applies

                case { Collection: "token", Error: FiReportCursorError.Malformed or FiReportCursorError.Tampered }:
                    tokenInvalid = true;
                    break;
                case { Error: not (FiReportCursorError.KeyInvalid or FiReportCursorError.Expired) }
                    when IsClient(e.Collection, e.Field):
                    client.Add((e.Field, e.Error.ToString()));
                    break;
                default:
                    return Problem(500, InternalError, [], details);
            }
        }

        if (client.Count > 0)
        {
            return Problem(400, ValidationFailed, client, details);
        }

        if (expired)
        {
            return Problem(409, ManifestExpired, [], details);
        }

        return Problem(400, TokenInvalid, [("token", tokenInvalid ? Invalid : "Required")], details);
    }

    private static bool IsClient(string? collection, string? field) =>
        collection == "binding" && field is not null && ClientFields.Contains(field);

    private static string Detail(string? collection, string? field, string error) => $"{collection}.{field}:{error}";

    private static FiReportProblem MissingInput(string what) =>
        Problem(500, InternalError, [], [Detail("problem", what, "Required")]);

    private static FiReportProblem Problem(int status, string code, IReadOnlyList<(string Field, string Error)> fields,
        IReadOnlyList<string> details)
    {
        var map = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var group in fields.GroupBy(f => f.Field, StringComparer.Ordinal))
        {
            map[group.Key] = group.Select(f => f.Error).Distinct(StringComparer.Ordinal).ToArray();
        }

        return new FiReportProblem(status, code, map, details);
    }
}
