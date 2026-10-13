using System.Text.Json.Serialization;

namespace MyERP.Modules.Inventory.Domain;

[JsonConverter(typeof(JsonStringEnumConverter<InventoryDocumentStatus>))]
public enum InventoryDocumentStatus
{
    Draft,
    PendingApproval,
    Approved,
    Rejected,
    Posted,
    Voided,
}

/// <summary>單據狀態與公司邊界的基線；實際保存與過帳在後續工作單元實作。</summary>
public sealed class InventoryDocument
{
    public InventoryDocument(long companyId, string documentType)
    {
        if (companyId <= 0 || string.IsNullOrWhiteSpace(documentType))
        {
            throw new ArgumentException("Company and document type are required.");
        }

        CompanyId = companyId;
        DocumentType = documentType;
    }

    public long CompanyId { get; }
    public string DocumentType { get; }
    public InventoryDocumentStatus Status { get; private set; } = InventoryDocumentStatus.Draft;

    public void Submit(long companyId) => Move(companyId, InventoryDocumentStatus.Draft, InventoryDocumentStatus.PendingApproval);
    public void Approve(long companyId) => Move(companyId, InventoryDocumentStatus.PendingApproval, InventoryDocumentStatus.Approved);
    public void Reject(long companyId) => Move(companyId, InventoryDocumentStatus.PendingApproval, InventoryDocumentStatus.Rejected);
    public void Post(long companyId) => Move(companyId, InventoryDocumentStatus.Approved, InventoryDocumentStatus.Posted);
    public void Void(long companyId) => Move(companyId, InventoryDocumentStatus.Draft, InventoryDocumentStatus.Voided);

    private void Move(long companyId, InventoryDocumentStatus from, InventoryDocumentStatus to)
    {
        if (companyId != CompanyId || Status != from)
        {
            throw new InvalidOperationException("Inventory document company or status does not allow this transition.");
        }

        Status = to;
    }
}
