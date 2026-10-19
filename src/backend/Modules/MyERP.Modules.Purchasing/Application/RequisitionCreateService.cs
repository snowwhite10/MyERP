using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Numbering;
using MyERP.Shared.Security;

namespace MyERP.Modules.Purchasing.Application;

public sealed record RequisitionLineInput(int LineNo, long ItemId, DateOnly RequiredDate, decimal Quantity, string? Remark);
public sealed record RequisitionInput(DateOnly BusinessDate, string? Remark, IReadOnlyList<RequisitionLineInput>? Lines);

/// <summary>REQ_F4114.3.2：SYS 單號、MD 物料與 PUR 草稿在同一交易內保存。</summary>
public sealed class RequisitionCreateService(MyErpDbContext db, RequisitionCompanyAccess access,
    ICurrentAuthorization authorization, INumberGenerator numbers, IRequisitionItemGuard items)
{
    public const string DocumentType = "PUR.REQUISITION";
    private const decimal MaxQuantity = 10_000_000_000_000m;

    public async Task<RequisitionDetail> CreateAsync(RequisitionInput input, CancellationToken token = default)
    {
        var companyId = access.RequireCompanyId();
        var owner = await authorization.GetAsync(token);
        if (owner is null || owner.CompanyId != companyId || !owner.Has("PUR.REQUISITION.CREATE"))
            throw new ForbiddenException("COMMON.PERMISSION.DENIED", "Requisition create permission is required.");
        var (remark, lines) = Validate(input);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        try
        {
            var result = await items.RunAsync(lines.Select(x => x.ItemId).Distinct().ToArray(), async () =>
            {
                var document = new Requisition
                {
                    CompanyId = companyId,
                    DocumentNo = await numbers.NextAsync(DocumentType, input.BusinessDate, token),
                    BusinessDate = input.BusinessDate,
                    RequesterId = owner.UserId,
                    BranchId = owner.BranchId,
                    DepartmentId = owner.DepartmentId,
                    Remark = remark,
                };
                foreach (var line in lines)
                {
                    line.CompanyId = companyId;
                    document.Lines.Add(line);
                }
                db.Add(document);
                await db.SaveChangesAsync(token);
                return new RequisitionDetail(document.Id, document.DocumentNo, document.BusinessDate,
                    document.RequesterId, document.Status, document.Remark, document.CreatedAt, document.Version,
                    [.. document.Lines.OrderBy(x => x.LineNo).Select(x => new RequisitionLineDetail(x.Id, x.LineNo,
                        x.ItemId, x.RequiredDate, x.Quantity, x.OrderedQuantity, x.Remark))],
                    document.BranchId, document.DepartmentId);
            }, token);
            await transaction.CommitAsync(token);
            return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw new ConflictException();
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private static (string? Remark, List<RequisitionLine> Lines) Validate(RequisitionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        void Fail(string field, string message) => errors.TryAdd(field, [message]);
        if (input.BusinessDate == default) Fail("businessDate", "Business date is required.");
        var remark = string.IsNullOrWhiteSpace(input.Remark) ? null : input.Remark.Trim();
        if (remark?.Length > 500) Fail("remark", "Remark must not exceed 500 characters.");
        if (input.Lines is not { Count: >= 1 and <= 1000 }) Fail("lines", "A requisition needs 1 to 1000 lines.");
        var lines = new List<RequisitionLine>();
        foreach (var (line, index) in (input.Lines ?? []).Select((line, index) => (line, index)))
        {
            var field = $"lines[{index}]";
            if (line is null) { Fail(field, "Line is required."); continue; }
            if (line.LineNo <= 0) Fail($"{field}.lineNo", "Line number must be positive.");
            if (line.ItemId <= 0) Fail($"{field}.itemId", "Item is required.");
            if (line.RequiredDate == default || line.RequiredDate < input.BusinessDate)
                Fail($"{field}.requiredDate", "Required date must not precede business date.");
            if (line.Quantity <= 0 || line.Quantity >= MaxQuantity || decimal.Round(line.Quantity, 6) != line.Quantity)
                Fail($"{field}.quantity", "Quantity must be positive, below 10^13, with at most 6 decimals.");
            var lineRemark = string.IsNullOrWhiteSpace(line.Remark) ? null : line.Remark.Trim();
            if (lineRemark?.Length > 500) Fail($"{field}.remark", "Remark must not exceed 500 characters.");
            lines.Add(new RequisitionLine { LineNo = line.LineNo, ItemId = line.ItemId,
                RequiredDate = line.RequiredDate, Quantity = line.Quantity, Remark = lineRemark });
        }
        if (lines.GroupBy(x => x.LineNo).Any(x => x.Count() > 1)) Fail("lines", "Line numbers must be unique.");
        if (errors.Count > 0) throw new RequestValidationException(errors);
        return (remark, lines);
    }
}
