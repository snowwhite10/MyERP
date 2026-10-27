using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.MasterData.Application;

public sealed class SalesItemLookup(MyErpDbContext db) : ISalesItemLookup
{
    public async Task<SalesItemIdentity> RequireActiveAsync(long id, CancellationToken token = default)
    {
        var item = await db.Set<Item>().AsNoTracking().Where(x => x.Id == id)
            .Select(x => new { x.Id, x.Code, x.Name, x.IsActive })
            .SingleOrDefaultAsync(token)
            ?? throw new NotFoundException("MD.ITEM.NOT_FOUND", "Item", id);
        if (!item.IsActive)
            throw new BusinessRuleException("MD.ITEM.INACTIVE", "Item is inactive.");
        return new SalesItemIdentity(item.Id, item.Code, item.Name);
    }
}
