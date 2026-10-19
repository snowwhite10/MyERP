using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.MasterData.Application;

public sealed class RequisitionItemGuard(MyErpDbContext db) : IRequisitionItemGuard
{
    public Task<T> RunAsync<T>(IReadOnlyCollection<long> itemIds, Func<Task<T>> action, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(itemIds);
        ArgumentNullException.ThrowIfNull(action);
        var companyId = MasterDataQueries.RequireCompany(db);
        return CompanyLock.RunSharedAsync(db, [CompanyLock.Item], companyId, async () =>
        {
            var ids = itemIds.Distinct().Order().ToArray();
            var found = await db.Set<Item>().AsNoTracking().Where(x => ids.Contains(x.Id))
                .Select(x => new { x.Id, x.IsActive }).ToListAsync(token);
            foreach (var id in ids)
            {
                var item = found.SingleOrDefault(x => x.Id == id)
                    ?? throw new NotFoundException("MD.ITEM.NOT_FOUND", "Item", id);
                if (!item.IsActive)
                    throw new BusinessRuleException("MD.ITEM.INACTIVE", $"Item {id} is inactive.");
            }
            return await action();
        }, token);
    }
}
