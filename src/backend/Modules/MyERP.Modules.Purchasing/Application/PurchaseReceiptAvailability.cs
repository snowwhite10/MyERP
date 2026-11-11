using MyERP.Shared.Errors;

namespace MyERP.Modules.Purchasing.Application;

public sealed record PurchaseReceiptOrderSnapshot(long Id, long CompanyId, string Status,
    bool IsDeleted);

public sealed record PurchaseReceiptOrderLineSnapshot(long Id, long PurchaseOrderId,
    long CompanyId, bool IsDeleted, decimal ApprovedQuantity, decimal PostedQuantity,
    decimal PendingQuantity);

public sealed record PurchaseReceiptAvailabilityLine(long PurchaseOrderLineId,
    decimal RequestedQuantity, decimal AvailableQuantity);

/// <summary>Pure preflight; the caller must load authoritative rows and repeat it under SQL locks.</summary>
public static class PurchaseReceiptAvailability
{
    private const decimal QuantityLimit = 10_000_000_000_000m;

    public static IReadOnlyList<PurchaseReceiptAvailabilityLine> Check(long companyId,
        PurchaseReceiptValidatedInput draft, PurchaseReceiptOrderSnapshot order,
        IReadOnlyList<PurchaseReceiptOrderLineSnapshot> sources)
    {
        if (companyId <= 0 || draft is null || order is null || sources is null ||
            draft.PurchaseOrderId <= 0 || order.Id != draft.PurchaseOrderId ||
            order.CompanyId != companyId || order.Status != "Approved" || order.IsDeleted ||
            draft.Lines is null || draft.Lines.Count is < 1 or > 1000)
            throw InvalidSource();

        var requested = new SortedDictionary<long, decimal>();
        var lineNumbers = new HashSet<int>();
        foreach (var line in draft.Lines)
        {
            if (line is null || line.LineNumber <= 0 || !lineNumbers.Add(line.LineNumber) ||
                line.PurchaseOrderLineId <= 0 || !ValidQuantity(line.Quantity, true))
                throw InvalidSource();
            try
            {
                requested[line.PurchaseOrderLineId] = checked(
                    requested.GetValueOrDefault(line.PurchaseOrderLineId) + line.Quantity);
            }
            catch (OverflowException) { throw InvalidSource(); }
            if (!ValidQuantity(requested[line.PurchaseOrderLineId], true))
                throw InvalidSource();
        }

        if (sources.Count != requested.Count)
            throw InvalidSource();
        var byId = new Dictionary<long, PurchaseReceiptOrderLineSnapshot>();
        foreach (var source in sources)
        {
            if (source is null || source.Id <= 0 || !requested.ContainsKey(source.Id) ||
                !byId.TryAdd(source.Id, source) || source.CompanyId != companyId ||
                source.PurchaseOrderId != order.Id || source.IsDeleted ||
                !ValidQuantity(source.ApprovedQuantity, true) ||
                !ValidQuantity(source.PostedQuantity, false) ||
                !ValidQuantity(source.PendingQuantity, false) ||
                source.PostedQuantity + source.PendingQuantity > source.ApprovedQuantity)
                throw InvalidSource();
        }

        var result = new List<PurchaseReceiptAvailabilityLine>(requested.Count);
        foreach (var (id, quantity) in requested)
        {
            var source = byId[id];
            var available = source.ApprovedQuantity - source.PostedQuantity - source.PendingQuantity;
            if (quantity > available)
                throw new BusinessRuleException("PUR.RECEIPT.QUANTITY_EXCEEDED",
                    $"Purchase order line {id} has {available} available; {quantity} requested.");
            result.Add(new(id, quantity, available));
        }
        return result.AsReadOnly();
    }

    private static bool ValidQuantity(decimal value, bool positive) =>
        value >= (positive ? 0.000001m : 0m) && value < QuantityLimit &&
        decimal.Round(value, 6) == value;

    private static BusinessRuleException InvalidSource() =>
        new("PUR.RECEIPT.INVALID_SOURCE", "Purchase order or line source is invalid.");
}
