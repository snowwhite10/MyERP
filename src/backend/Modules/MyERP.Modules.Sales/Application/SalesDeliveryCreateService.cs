using System.Data;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Numbering;

namespace MyERP.Modules.Sales.Application;

/// <summary>
/// REQ_F4112.3.2 / BR-SD-021–023: creates a Draft delivery from one visible Approved order.
/// A Draft reserves nothing; the create check rejects quantities above
/// ordered − Posted − active Pending so an obviously unshippable Draft is never saved.
/// Submission (WU-SD-004a2b13) locks source lines and rechecks the balance before reserving it.
/// </summary>
public sealed class SalesDeliveryCreateService(MyErpDbContext db, SalesActionAccess access,
    SalesCompanyAccess company, INumberGenerator numbers, SalesDeliveryDraftLines draftLines)
{
    public const string DocumentType = "SAL.DELIVERY";

    public async Task<SalesDelivery> CreateAsync(SalesDeliveryDraftInput input,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        input.Validate();
        var scope = await access.RequireScopeAsync("SAL.DELIVERY.CREATE", token);
        var companyId = company.RequireCompanyId();
        // ADR-138: Read Committed plus a primary-key update lock on the source order.
        // Creates for the same order serialize on that row; a Serializable balance join
        // would range-lock other orders' and companies' deliveries and deadlock inserts.
        // The balance here is advisory because a Draft reserves nothing; submit rechecks it.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        try
        {
            await draftLines.LockOrderAsync(companyId, input.OrderId, token);
            var order = await scope.Apply(db.Set<SalesOrder>()).AsNoTracking()
                .Where(x => x.Id == input.OrderId)
                .Select(x => new { x.Id, x.Status }).SingleOrDefaultAsync(token)
                ?? throw new NotFoundException("SAL.ORDER.NOT_FOUND", "Sales order", input.OrderId);
            if (order.Status != SalesOrderStatus.Approved)
                throw new BusinessRuleException("SAL.DELIVERY.ORDER_NOT_APPROVED",
                    "The source order must be approved before delivery.");

            var delivery = new SalesDelivery
            {
                CompanyId = companyId,
                DocumentNo = string.Empty,
                OrderId = order.Id,
                BusinessDate = input.BusinessDate
            };
            delivery.Lines.AddRange(await draftLines.BuildAsync(companyId, order.Id, null, input.Lines!, token));
            await draftLines.GuardAsync(delivery.Lines, async () =>
            {
                delivery.DocumentNo = await numbers.NextAsync(DocumentType, input.BusinessDate, token);
                db.Add(delivery);
                await db.SaveChangesAsync(token);
            }, token);
            await transaction.CommitAsync(token);
            return delivery;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }
}
