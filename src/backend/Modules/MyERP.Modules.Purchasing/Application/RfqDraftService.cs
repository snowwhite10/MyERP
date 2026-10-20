using System.Data;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Numbering;
using MyERP.Shared.Security;
using MyERP.Shared.Time;

namespace MyERP.Modules.Purchasing.Application;

public sealed record RfqLineInput(int LineNo, long ItemId, long? RequisitionLineId,
    DateOnly RequiredDate, decimal Quantity, string? Remark);
public sealed record RfqInput(DateOnly BusinessDate, DateOnly ReplyDeadline, string? Remark,
    IReadOnlyList<RfqLineInput>? Lines, IReadOnlyList<long>? SupplierIds);

/// <summary>詢價草稿整單交易；MD 引用經公開契約檢查，請購來源只查 PUR 自有表。</summary>
public sealed class RfqDraftService(MyErpDbContext db, RequisitionCompanyAccess access,
    ICurrentAuthorization authorization, INumberGenerator numbers, IRequisitionItemGuard items,
    ISupplierLookup suppliers, IClock clock)
{
    public const string DocumentType = "PUR.RFQ";
    private const decimal MaxQuantity = 10_000_000_000_000m;

    public async Task<RfqDetail> CreateAsync(RfqInput input, CancellationToken token = default)
    {
        var companyId = access.RequireCompanyId();
        var owner = await RequireScopeAsync(PurchasingPermissions.RfqCreate, companyId, token);
        var (remark, lines, supplierIds) = Validate(input);
        return await InTransactionAsync(async () => await items.RunAsync(
            lines.Select(x => x.ItemId).Distinct().ToArray(), async () =>
            {
                await ValidateReferencesAsync(lines, supplierIds, token);
                var document = new Rfq
                {
                    CompanyId = companyId,
                    DocumentNo = await numbers.NextAsync(DocumentType, input.BusinessDate, token),
                    BusinessDate = input.BusinessDate,
                    ReplyDeadline = input.ReplyDeadline,
                    OwnerId = owner.UserId,
                    BranchId = owner.BranchId,
                    DepartmentId = owner.DepartmentId,
                    Remark = remark
                };
                foreach (var line in lines) { line.CompanyId = companyId; document.Lines.Add(line); }
                foreach (var id in supplierIds)
                    document.Invitations.Add(new RfqInvitation { CompanyId = companyId, SupplierId = id });
                db.Add(document);
                await db.SaveChangesAsync(token);
                return Detail(document);
            }, token), token);
    }

    public async Task<RfqDetail> UpdateAsync(long id, int version, RfqInput input,
        CancellationToken token = default)
    {
        var companyId = access.RequireCompanyId();
        var scope = await RequireScopeAsync(PurchasingPermissions.RfqEdit, companyId, token);
        var (remark, lines, supplierIds) = Validate(input);
        return await InTransactionAsync(async () =>
        {
            var document = await LoadAsync(id, version, scope, PurchasingPermissions.RfqEdit, token);
            if (document.Status != RfqStatus.Draft)
                throw new BusinessRuleException("PUR.RFQ.NOT_EDITABLE", "Only draft RFQs can be edited.");
            return await items.RunAsync(lines.Select(x => x.ItemId).Distinct().ToArray(), async () =>
            {
                await ValidateReferencesAsync(lines, supplierIds, token);
                db.RemoveRange(document.Lines);
                db.RemoveRange(document.Invitations);
                document.Lines.Clear();
                document.Invitations.Clear();
                foreach (var line in lines) { line.CompanyId = companyId; document.Lines.Add(line); }
                foreach (var supplierId in supplierIds)
                    document.Invitations.Add(new RfqInvitation { CompanyId = companyId, SupplierId = supplierId });
                document.BusinessDate = input.BusinessDate;
                document.ReplyDeadline = input.ReplyDeadline;
                document.Remark = remark;
                // Replacing children must advance the parent version even when header values are unchanged.
                db.Entry(document).Property(x => x.Remark).IsModified = true;
                await db.SaveChangesAsync(token);
                return Detail(document);
            }, token);
        }, token);
    }

    public async Task DeleteAsync(long id, int version, CancellationToken token = default)
    {
        var scope = await RequireScopeAsync(PurchasingPermissions.RfqDelete, access.RequireCompanyId(), token);
        await InTransactionAsync(async () =>
        {
            var document = await LoadAsync(id, version, scope, PurchasingPermissions.RfqDelete, token);
            if (document.Status != RfqStatus.Draft)
                throw new BusinessRuleException("PUR.RFQ.NOT_DELETABLE", "Only draft RFQs can be deleted.");
            document.IsDeleted = true;
            document.DeletedAt = clock.LocalNow;
            await db.SaveChangesAsync(token);
            return true;
        }, token);
    }

    private async Task<UserCompanyAuthorization> RequireScopeAsync(string permission, long companyId,
        CancellationToken token)
    {
        var owner = await authorization.GetAsync(token);
        if (owner is null || owner.CompanyId != companyId || !owner.Has(permission) ||
            owner.FilterFor(permission) is null)
            throw new ForbiddenException("COMMON.PERMISSION.DENIED", "RFQ action permission is required.");
        return owner;
    }

    private async Task<Rfq> LoadAsync(long id, int version, UserCompanyAuthorization owner, string permission,
        CancellationToken token)
    {
        var scope = owner.FilterFor(permission)!;
        IQueryable<Rfq> query = db.Set<Rfq>();
        query = scope.EffectiveScope switch
        {
            DataScope.Company => query,
            DataScope.Branch => query.Where(x => x.BranchId == scope.BranchId),
            DataScope.Department => query.Where(x => x.DepartmentId == scope.DepartmentId),
            _ => query.Where(x => x.OwnerId == scope.UserId)
        };
        var document = await query.Include(x => x.Lines).Include(x => x.Invitations)
            .AsSplitQuery().SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new NotFoundException("PUR.RFQ.NOT_FOUND", "Request for quotation", id);
        if (version < 1 || document.Version != version) throw new ConflictException();
        return document;
    }

    private async Task ValidateReferencesAsync(List<RfqLine> lines, long[] supplierIds,
        CancellationToken token)
    {
        foreach (var id in supplierIds) await suppliers.RequireApprovedAsync(id, token);
        foreach (var (line, index) in lines.Select((line, index) => (line, index)))
        {
            if (line.RequisitionLineId is not { } sourceId) continue;
            var source = await db.Set<RequisitionLine>().AsNoTracking()
                .Where(x => x.Id == sourceId && x.CompanyId == access.RequireCompanyId())
                .Select(x => new { x.ItemId, x.Quantity, x.OrderedQuantity,
                    RequisitionStatus = db.Set<Requisition>()
                        .Where(r => r.Id == x.RequisitionId).Select(r => r.Status).FirstOrDefault() })
                .SingleOrDefaultAsync(token)
                ?? throw new NotFoundException("PUR.REQUISITION.LINE_NOT_FOUND", "Requisition line", sourceId);
            if (source.RequisitionStatus != RequisitionStatus.Approved || source.ItemId != line.ItemId ||
                line.Quantity > source.Quantity - source.OrderedQuantity)
                throw new BusinessRuleException("PUR.RFQ.INVALID_SOURCE",
                    $"Line {index + 1} needs an approved requisition line with the same item and enough remaining quantity.");
        }
    }

    private async Task<T> InTransactionAsync<T>(Func<Task<T>> action, CancellationToken token)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        try
        {
            var result = await action();
            await transaction.CommitAsync(token);
            return result;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            if (ex is DbUpdateConcurrencyException) throw new ConflictException();
            throw;
        }
    }

    internal static (string? Remark, List<RfqLine> Lines, long[] SupplierIds) Validate(RfqInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        void Fail(string field, string message) => errors.TryAdd(field, [message]);
        if (input.BusinessDate == default) Fail("businessDate", "Business date is required.");
        if (input.ReplyDeadline == default || input.ReplyDeadline < input.BusinessDate)
            Fail("replyDeadline", "Reply deadline must not precede business date.");
        var remark = string.IsNullOrWhiteSpace(input.Remark) ? null : input.Remark.Trim();
        if (remark?.Length > 500) Fail("remark", "Remark must not exceed 500 characters.");
        if (input.Lines is not { Count: >= 1 and <= 1000 }) Fail("lines", "An RFQ needs 1 to 1000 lines.");
        if (input.SupplierIds is not { Count: >= 1 and <= 50 })
            Fail("supplierIds", "An RFQ needs 1 to 50 suppliers.");
        var supplierIds = input.SupplierIds?.ToArray() ?? [];
        if (supplierIds.Any(x => x <= 0) || supplierIds.Distinct().Count() != supplierIds.Length)
            Fail("supplierIds", "Suppliers must be positive and unique.");
        var lines = new List<RfqLine>();
        foreach (var (line, index) in (input.Lines ?? []).Select((line, index) => (line, index)))
        {
            var field = $"lines[{index}]";
            if (line is null) { Fail(field, "Line is required."); continue; }
            if (line.LineNo <= 0) Fail($"{field}.lineNo", "Line number must be positive.");
            if (line.ItemId <= 0) Fail($"{field}.itemId", "Item is required.");
            if (line.RequisitionLineId is <= 0) Fail($"{field}.requisitionLineId", "Source must be positive.");
            if (line.RequiredDate == default || line.RequiredDate < input.BusinessDate)
                Fail($"{field}.requiredDate", "Required date must not precede business date.");
            if (line.Quantity <= 0 || line.Quantity >= MaxQuantity || decimal.Round(line.Quantity, 6) != line.Quantity)
                Fail($"{field}.quantity", "Quantity must be positive, below 10^13, with at most 6 decimals.");
            var lineRemark = string.IsNullOrWhiteSpace(line.Remark) ? null : line.Remark.Trim();
            if (lineRemark?.Length > 500) Fail($"{field}.remark", "Remark must not exceed 500 characters.");
            lines.Add(new RfqLine { LineNo = line.LineNo, ItemId = line.ItemId,
                RequisitionLineId = line.RequisitionLineId, RequiredDate = line.RequiredDate,
                Quantity = line.Quantity, Remark = lineRemark });
        }
        if (lines.GroupBy(x => x.LineNo).Any(x => x.Count() > 1)) Fail("lines", "Line numbers must be unique.");
        if (errors.Count > 0) throw new RequestValidationException(errors);
        return (remark, lines, supplierIds);
    }

    private static RfqDetail Detail(Rfq document) => new(document.Id, document.DocumentNo,
        document.BusinessDate, document.ReplyDeadline, document.OwnerId, document.Status,
        document.Remark, document.SentAt, document.CreatedAt, document.Version,
        document.BranchId, document.DepartmentId,
        [.. document.Lines.OrderBy(x => x.LineNo).Select(x => new RfqLineDetail(x.Id, x.LineNo,
            x.ItemId, x.RequisitionLineId, x.RequiredDate, x.Quantity, x.Remark))],
        [.. document.Invitations.OrderBy(x => x.SupplierId).Select(x => new RfqInvitationDetail(
            x.Id, x.SupplierId, x.SupplierCodeSnapshot, x.SupplierNameSnapshot, x.SentAt))]);
}
