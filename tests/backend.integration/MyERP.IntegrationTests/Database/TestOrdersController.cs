using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.IntegrationTests.Database;

public sealed record TestOrderInput(string DocNo, decimal Amount, int Version);

public sealed record TestOrderDto(long Id, string DocNo, decimal Amount, int Version);

/// <summary>只在 <see cref="DatabaseApiFactory"/> 中載入的測試 API：驗證 DbContext、interceptor、例外對應在 HTTP 管線中生效。</summary>
[ApiController]
[Route("api/v1/tst/orders")]
public sealed class TestOrdersController(MyErpDbContext db) : ControllerBase
{
    [HttpPost]
    public async Task<TestOrderDto> Create(TestOrderInput input, CancellationToken cancellationToken)
    {
        var order = new TestOrder { DocNo = input.DocNo, Amount = input.Amount };
        db.Add(order);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(order);
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<TestOrderDto>> Get(long id, CancellationToken cancellationToken)
    {
        var order = await db.Set<TestOrder>().AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        return order is null ? NotFound() : ToDto(order);
    }

    [HttpPut("{id:long}")]
    public async Task<ActionResult<TestOrderDto>> Update(long id, TestOrderInput input, CancellationToken cancellationToken)
    {
        var order = await db.Set<TestOrder>().FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        db.Entry(order).Property(o => o.Version).OriginalValue = input.Version;
        order.DocNo = input.DocNo;
        order.Amount = input.Amount;
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(order);
    }

    private static TestOrderDto ToDto(TestOrder order) => new(order.Id, order.DocNo, order.Amount, order.Version);
}
