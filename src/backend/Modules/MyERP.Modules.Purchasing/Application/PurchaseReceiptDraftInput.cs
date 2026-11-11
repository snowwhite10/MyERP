using MyERP.Shared.Errors;

namespace MyERP.Modules.Purchasing.Application;

public sealed record PurchaseReceiptDraftLine(int LineNumber, long PurchaseOrderLineId,
    decimal Quantity);

public sealed record PurchaseReceiptDraftInput(long PurchaseOrderId, DateOnly BusinessDate,
    IReadOnlyList<PurchaseReceiptDraftLine>? Lines)
{
    private const decimal QuantityLimit = 10_000_000_000_000m;

    /// <summary>Validates client shape only; SQL must recheck order and available quantity under lock.</summary>
    public static PurchaseReceiptValidatedInput Validate(PurchaseReceiptDraftInput? input)
    {
        if (input is null || input.PurchaseOrderId <= 0 || input.BusinessDate == default)
            throw new BusinessRuleException("PUR.RECEIPT.INVALID_HEADER", "Receipt header is invalid.");
        if (input.Lines is null || input.Lines.Count is < 1 or > 1000)
            throw InvalidLines();

        var numbers = new HashSet<int>();
        var lines = new List<PurchaseReceiptDraftLine>(input.Lines.Count);
        foreach (var line in input.Lines)
        {
            if (line is null || line.LineNumber <= 0 || !numbers.Add(line.LineNumber) ||
                line.PurchaseOrderLineId <= 0 || line.Quantity <= 0 ||
                line.Quantity >= QuantityLimit || decimal.Round(line.Quantity, 6) != line.Quantity)
                throw InvalidLines();
            lines.Add(line);
        }

        lines.Sort((left, right) => left.LineNumber.CompareTo(right.LineNumber));
        return new(input.PurchaseOrderId, input.BusinessDate, lines.AsReadOnly());
    }

    public static void EnsureEditable(string? status)
    {
        if (status is not ("Draft" or "Rejected"))
            throw new BusinessRuleException("PUR.RECEIPT.STATUS_NOT_EDITABLE",
                "Only draft or rejected receipts can be revised.");
    }

    private static BusinessRuleException InvalidLines() =>
        new("PUR.RECEIPT.INVALID_LINES", "Receipt lines have invalid numbers or quantities.");
}

public sealed record PurchaseReceiptValidatedInput(long PurchaseOrderId, DateOnly BusinessDate,
    IReadOnlyList<PurchaseReceiptDraftLine> Lines);
