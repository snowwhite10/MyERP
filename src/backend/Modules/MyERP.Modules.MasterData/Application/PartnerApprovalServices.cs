using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Approvals;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.MasterData.Application;

/// <summary>客戶送審（SYS 單據類型 MD.CUSTOMER）。</summary>
public sealed class CustomerApprovalService(MyErpDbContext db, IApprovalWorkflowService workflow)
    : PartnerApprovalService<Customer, CustomerCategory>(db, workflow, DocumentType, "Customer")
{
    public const string DocumentType = "MD.CUSTOMER";
}

/// <summary>供應商送審（SYS 單據類型 MD.SUPPLIER）。</summary>
public sealed class SupplierApprovalService(MyErpDbContext db, IApprovalWorkflowService workflow)
    : PartnerApprovalService<Supplier, SupplierCategory>(db, workflow, DocumentType, "Supplier")
{
    public const string DocumentType = "MD.SUPPLIER";
}

/// <summary>SYS 最終審批結果在同一交易內同步到客戶主檔。</summary>
public sealed class CustomerApprovalResultHandler(MyErpDbContext db)
    : PartnerApprovalResultHandler<Customer, CustomerCategory>(db, CustomerApprovalService.DocumentType, "Customer");

/// <summary>SYS 最終審批結果在同一交易內同步到供應商主檔。</summary>
public sealed class SupplierApprovalResultHandler(MyErpDbContext db)
    : PartnerApprovalResultHandler<Supplier, SupplierCategory>(db, SupplierApprovalService.DocumentType, "Supplier");

/// <summary>
/// 往來對象送審。SYS 審批引擎和 MD 共用 scoped DbContext 與交易；單據類型同時作為錯誤碼前綴（ADR-046）。
/// </summary>
public abstract class PartnerApprovalService<TPartner, TCategory>(
    MyErpDbContext db, IApprovalWorkflowService workflow, string documentType, string resourceName)
    where TPartner : BusinessPartner<TCategory>
    where TCategory : MasterCategory
{
    public async Task<ApprovalState> SubmitAsync(long id, int version, CancellationToken token = default)
    {
        if (db.CurrentCompanyId is not > 0)
            throw new ForbiddenException("COMMON.COMPANY.ACCESS_DENIED", "Current company is required.");
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var partner = await db.Set<TPartner>().SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new NotFoundException(documentType + ".NOT_FOUND", resourceName, id);
        if (version < 1 || partner.Version != version) throw new ConflictException();
        if (partner.ReviewStatus == PartnerReviewStatus.Approved)
            throw new BusinessRuleException(documentType + ".ALREADY_APPROVED", resourceName + " is already approved.");

        var state = await workflow.SubmitAsync(new ApprovalSubmission(documentType, id, partner.Code, 0m, partner.Name), token);
        partner.ReviewStatus = state.Status == ApprovalStatus.Approved
            ? PartnerReviewStatus.Approved : PartnerReviewStatus.Pending;
        db.Entry(partner).Property(x => x.Version).OriginalValue = version;
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException) { throw new ConflictException(); }
        await transaction.CommitAsync(token);
        return state;
    }
}

/// <summary>只接受待審資料的最終核准／駁回；不呼叫 SaveChanges，由 SYS 審批交易一併提交。</summary>
public abstract class PartnerApprovalResultHandler<TPartner, TCategory>(MyErpDbContext db, string documentType, string resourceName)
    : IApprovalResultHandler
    where TPartner : BusinessPartner<TCategory>
    where TCategory : MasterCategory
{
    public string DocumentType => documentType;

    public async Task HandleAsync(ApprovalCompleted result, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Status is not (ApprovalStatus.Approved or ApprovalStatus.Rejected))
            throw new ArgumentOutOfRangeException(nameof(result));
        var partner = await db.Set<TPartner>().SingleOrDefaultAsync(x => x.Id == result.DocumentId, cancellationToken)
            ?? throw new NotFoundException(documentType + ".NOT_FOUND", resourceName, result.DocumentId);
        if (partner.ReviewStatus != PartnerReviewStatus.Pending)
            throw new ConflictException(documentType + ".APPROVAL_STATE_CHANGED", resourceName + " review state changed.");
        partner.ReviewStatus = result.Status == ApprovalStatus.Approved
            ? PartnerReviewStatus.Approved : PartnerReviewStatus.Rejected;
    }
}
