namespace MyERP.Modules.MasterData.Contracts;

/// <summary>
/// Display label of a current-company customer. Inactive, unapproved and soft-deleted customers are still labelled
/// so history (e.g. FI credit limits) stays readable; <see cref="IsActive"/>/<see cref="IsDeleted"/> let the caller
/// mark them (ADR-077).
/// </summary>
public sealed record CustomerLabel(long Id, string Code, string Name, bool IsActive, bool IsDeleted);

/// <summary>
/// Read-only batch customer labels for list/detail screens of other modules, so they never read MD tables.
/// Only the current company's customers are returned; ids of another company or unknown ids are simply absent.
/// </summary>
public interface ICustomerLabels
{
    Task<IReadOnlyDictionary<long, CustomerLabel>> GetLabelsAsync(IReadOnlyCollection<long> ids,
        CancellationToken token = default);
}
