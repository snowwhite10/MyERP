using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Errors;

namespace MyERP.Modules.Sales.Application;

/// <summary>Checks the source shape of a prepared order before it enters the draft store.</summary>
public static class SalesOrderCreateSource
{
    public static void Validate(SalesOrder draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (draft.Status != SalesOrderStatus.Draft || draft.IsDeleted)
            throw new BusinessRuleException("SAL.ORDER.INVALID_STATUS", "Only an active Draft can be created.");
        if (draft.Lines.Count is < 1 or > 1000)
            throw InvalidSource();

        if (draft.SourceQuotationId is null)
        {
            if (draft.Lines.Any(line => line.SourceQuotationLineId is not null))
                throw InvalidSource();
            return;
        }

        if (draft.SourceQuotationId <= 0 ||
            draft.Lines.Any(line => line.SourceQuotationLineId is null or <= 0) ||
            draft.Lines.Select(line => line.SourceQuotationLineId).Distinct().Count() != draft.Lines.Count)
            throw InvalidSource();
    }

    private static BusinessRuleException InvalidSource() =>
        new("SAL.ORDER.INVALID_SOURCE", "Order quotation source IDs are incomplete or inconsistent.");
}
