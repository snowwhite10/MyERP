using MyERP.Modules.Finance.Domain;

namespace MyERP.Modules.Finance.Application;

/// <summary>
/// Current FI account-mapping version for one source type (ADR-103). Each FI-owned source adapter that builds GL
/// drafts from a versioned mapping (BR-FI-052/060/137) registers exactly one scoped implementation; the posting SQL
/// port reads it inside the posting transaction, after both posting locks, through the attempt's own scope.
/// </summary>
public interface IGlPostingMappingVersionSource
{
    /// <summary>Source module this mapping serves (ordinal match with <see cref="GlPostingSource.Module"/>).</summary>
    string SourceModule { get; }

    /// <summary>Source type this mapping serves (ordinal match with <see cref="GlPostingSource.SourceType"/>).</summary>
    string SourceType { get; }

    /// <summary>Current mapping version of the company; 0 or less means no usable mapping (the decision rejects it).</summary>
    Task<int> ReadCurrentVersionAsync(long companyId, CancellationToken cancellationToken);
}

/// <summary>WU-FI-004a3x1b2b1: picks the mapping-version source for a posting source (ADR-103).</summary>
public static class GlPostingMappingVersionRules
{
    /// <summary>
    /// The single registered source whose module and type equal the posting source (ordinal). None or more than one
    /// is a configuration fault: null, which the SQL port reports as an untrusted authority (500, no write).
    /// </summary>
    public static IGlPostingMappingVersionSource? Select(IEnumerable<IGlPostingMappingVersionSource?>? sources,
        GlPostingSource? source)
    {
        if (sources is null || source is null)
            return null;
        IGlPostingMappingVersionSource? match = null;
        foreach (var candidate in sources)
        {
            if (candidate is null
                || !string.Equals(candidate.SourceModule, source.Module, StringComparison.Ordinal)
                || !string.Equals(candidate.SourceType, source.SourceType, StringComparison.Ordinal))
                continue;
            if (match is not null)
                return null;
            match = candidate;
        }
        return match;
    }
}
