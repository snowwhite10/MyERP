using Microsoft.EntityFrameworkCore;
using Moq;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Sales;
using MyERP.Modules.Sales.Application;
using MyERP.Modules.Sales.Domain;
using MyERP.Modules.System.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// SD-RETURN-EDIT-001～005 (WU-SD-008a2b1, ADR-186): Draft return edit and soft delete on real SQL — versioned line
/// replacement with the creation checks and snapshot rerun, immutable request key and order, state/scope/permission
/// refusals, soft delete reserving the key, rollback of a failed edit and the return-row lock.
/// </summary>
public sealed partial class SalesReturnCreateServiceTests
{
    private static readonly DateTime EditNow = new(2028, 3, 5, 14, 30, 0);

    [Fact]
    public async Task EditReplacesEveryLineResnapshotsAndAdvancesTheVersion()
    {
        // SD-RETURN-EDIT-001: new date, remark and lines; amounts recomputed; number, key and fingerprint kept.
        var s = await SeedAsync("EA");
        var input = new SalesReturnDraftInput("E-1", Day.AddDays(1), "first",
        [
            new SalesReturnLineInput(1, s.DeliveryA, 51, null, 0.5m, [new(s.InvoiceLineA, 1m)]),
            new SalesReturnLineInput(2, s.DeliveryB, 52, 9, 1m, null)
        ]);
        var created = await CreateAsync(s, input);
        var guard = new RecordingGuard();

        var edited = await UpdateAsync(s, created.Return.Id, 1, new SalesReturnDraftInput("E-1", Day.AddDays(2),
            "  second  ", [new SalesReturnLineInput(3, s.DeliveryB, 53, null, 0.5m, null)]), guard);

        Assert.Equal((created.Return.Id, "EA001", 2, Day.AddDays(2), "second"),
            (edited.Id, edited.DocumentNo, edited.Version, edited.BusinessDate, edited.Remark));
        await using var verify = Context(s.CompanyId);
        var saved = await verify.Set<SalesReturn>().AsNoTracking().Include(x => x.Lines)
            .ThenInclude(x => x.InvoiceAllocations).SingleAsync(x => x.Id == created.Return.Id, Ct);
        Assert.Equal(("E-1", input.Fingerprint(), SalesReturnStatus.Draft, 2, (long?)7),
            (saved.RequestKey, saved.RequestFingerprint, saved.Status, saved.Version, saved.UpdatedBy));
        // Unbilled 0.5 of order line B's 1.5 uninvoiced units: 15.00 ÷ 3, 0.75 ÷ 3, 465 ÷ 3, 23 ÷ 3 → 7.67 → 8 TWD.
        var line = Assert.Single(saved.Lines);
        AssertLine(line, 3, s.Delivery.Id, s.DeliveryB, s.Order.Lines[1].Id, 32, 502, 53, null, 0.5m, 0.5m,
            5.00m, 0.25m, 155m, 8m);
        Assert.Empty(line.InvoiceAllocations);
        Assert.False(await verify.Set<SalesReturnInvoiceAllocation>().AnyAsync(Ct));
        Assert.Equal((5.00m, 0.25m, 5.25m, 155m, 8m, 163m), (saved.OriginalNet, saved.OriginalTax,
            saved.OriginalGross, saved.BaseNet, saved.BaseTax, saved.BaseGross));
        Assert.Equal([(32L, 53L, (long?)null)], guard.Calls);
        // The edit took no new number; a create retry of the original payload replays the edited return.
        Assert.Equal(1, (await verify.Set<NumberSequence>().SingleAsync(Ct)).CurrentValue);
        var replay = await CreateAsync(s, input);
        Assert.Equal((true, created.Return.Id, 3), (replay.Replayed, replay.Return.Id,
            Assert.Single(replay.Return.Lines).LineNo));
    }

    [Fact]
    public async Task EditRerunsTheCreationChecksAgainstTheSavedOrderAndCommittedReturns()
    {
        // SD-RETURN-EDIT-002: version, key, order, balance and date refusals leave the Draft unchanged.
        var s = await SeedAsync("EB");
        var sibling = await SeedAsync("EC", sameCompany: s.CompanyId);
        var created = await CreateAsync(s, Line("B-1", s.DeliveryB, 1m));
        var id = created.Return.Id;

        Assert.Equal(CommonErrorCodes.ConcurrencyConflict, (await Assert.ThrowsAsync<ConflictException>(() =>
            UpdateAsync(s, id, 2, Line("B-1", s.DeliveryB, 0.5m)))).ErrorCode);
        Assert.Equal(CommonErrorCodes.ConcurrencyConflict, (await Assert.ThrowsAsync<ConflictException>(() =>
            UpdateAsync(s, id, 0, Line("B-1", s.DeliveryB, 0.5m)))).ErrorCode);
        Assert.Equal("SAL.RETURN.REQUEST_KEY_IMMUTABLE", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            UpdateAsync(s, id, 1, Line("B-other", s.DeliveryB, 0.5m)))).ErrorCode);
        var mixed = await Assert.ThrowsAsync<BusinessRuleException>(() => UpdateAsync(s, id, 1,
            new SalesReturnDraftInput("B-1", Day.AddDays(1), null, [new(4, sibling.DeliveryB, 51, null, 1m, null)])));
        Assert.Equal(("SAL.RETURN.SOURCE_MIXED", 4), (mixed.ErrorCode, mixed.Extensions!["lineNo"]));
        var early = await Assert.ThrowsAsync<BusinessRuleException>(() => UpdateAsync(s, id, 1,
            new SalesReturnDraftInput("B-1", Day.AddDays(-1), null, [new(2, s.DeliveryB, 51, null, 1m, null)])));
        Assert.Equal(("SAL.RETURN.DATE_BEFORE_DELIVERY", 2), (early.ErrorCode, early.Extensions!["lineNo"]));
        Assert.Equal("SAL.RETURN.TRACKING_UNSUPPORTED", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            UpdateAsync(s, id, 1, Line("B-1", s.DeliveryB, 1m), new RecordingGuard { TrackedItem = 32 }))).ErrorCode);
        await Assert.ThrowsAsync<RequestValidationException>(() => UpdateAsync(s, id, 1, Line("B-1", s.DeliveryB, 0m)));

        // Another Posted return of the whole line B leaves nothing; the Draft itself never counted.
        var other = await CreateAsync(s, Line("B-2", s.DeliveryB, 1.5m));
        await PostAsync(s.CompanyId, other.Return.Id, creditNoteId: null);
        var exceeded = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            UpdateAsync(s, id, 1, Line("B-1", s.DeliveryB, 0.5m)));
        Assert.Equal(("SAL.RETURN.RETURN_EXCEEDED", 0m), (exceeded.ErrorCode, exceeded.Extensions!["remaining"]));

        // Edit after cancellation of the order is refused as on create.
        await using (var db = Context(s.CompanyId))
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE dbo.sal_order SET status = 'Cancelled' WHERE id = {0}", [s.Order.Id], Ct);
        Assert.Equal("SAL.RETURN.ORDER_NOT_APPROVED", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            UpdateAsync(s, id, 1, Line("B-1", s.DeliveryA, 0.5m)))).ErrorCode);

        await AssertUnchangedAsync(s, id, s.DeliveryB, 1m);
    }

    [Fact]
    public async Task HiddenForeignUnauthorisedAndSubmittedReturnsCannotBeEditedOrDeleted()
    {
        // SD-RETURN-EDIT-003: 404 for unknown, other-company and out-of-scope returns, 403 without permission and
        // INVALID_STATUS for Pending, Rejected and Posted returns.
        var s = await SeedAsync("ED");
        var foreign = await SeedAsync("EE");
        var created = await CreateAsync(s, Line("D-1", s.DeliveryB, 1m));
        var id = created.Return.Id;
        var foreignReturn = await CreateAsync(foreign, Line("D-9", foreign.DeliveryB, 1m));

        foreach (var hidden in new[] { long.MaxValue, foreignReturn.Return.Id })
        {
            Assert.Equal("SAL.RETURN.NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() =>
                UpdateAsync(s, hidden, 1, Line("D-1", s.DeliveryB, 0.5m)))).ErrorCode);
            await Assert.ThrowsAsync<NotFoundException>(() => DeleteAsync(s, hidden, 1));
        }
        await Assert.ThrowsAsync<NotFoundException>(() =>
            UpdateAsync(s, id, 1, Line("D-1", s.DeliveryB, 0.5m), userId: 8, scope: DataScope.Self));
        await Assert.ThrowsAsync<NotFoundException>(() => DeleteAsync(s, id, 1, userId: 8, scope: DataScope.Self));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            UpdateAsync(s, id, 1, Line("D-1", s.DeliveryB, 0.5m), allow: false));
        await Assert.ThrowsAsync<ForbiddenException>(() => DeleteAsync(s, id, 1, allow: false));

        foreach (var status in new[] { "Pending", "Rejected" })
        {
            await SetStatusAsync(s.CompanyId, id, status);
            Assert.Equal("SAL.RETURN.INVALID_STATUS", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
                UpdateAsync(s, id, 1, Line("D-1", s.DeliveryB, 0.5m)))).ErrorCode);
            Assert.Equal("SAL.RETURN.INVALID_STATUS",
                (await Assert.ThrowsAsync<BusinessRuleException>(() => DeleteAsync(s, id, 1))).ErrorCode);
        }
        await SetStatusAsync(s.CompanyId, id, "Draft");
        await PostAsync(s.CompanyId, id, creditNoteId: null);
        Assert.Equal("SAL.RETURN.INVALID_STATUS", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            UpdateAsync(s, id, 1, Line("D-1", s.DeliveryB, 0.5m)))).ErrorCode);
        Assert.Equal("SAL.RETURN.INVALID_STATUS",
            (await Assert.ThrowsAsync<BusinessRuleException>(() => DeleteAsync(s, id, 1))).ErrorCode);

        await using var verify = Context(s.CompanyId);
        var saved = await verify.Set<SalesReturn>().AsNoTracking().SingleAsync(x => x.Id == id, Ct);
        Assert.Equal((SalesReturnStatus.Posted, false), (saved.Status, saved.IsDeleted));
    }

    [Fact]
    public async Task DeleteSoftDeletesTheDraftAndKeepsItsRequestKeyReserved()
    {
        // SD-RETURN-EDIT-004: stale version 409; deleted Draft keeps lines, gets IClock time, is 404 afterwards and its
        // key is 409 on create. A Draft of a cancelled order may still be deleted.
        var s = await SeedAsync("EF");
        var input = Line("F-1", s.DeliveryA, 0.5m, (s.InvoiceLineA, 0.5m));
        var created = await CreateAsync(s, input);
        var id = created.Return.Id;
        await using (var db = Context(s.CompanyId))
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE dbo.sal_order SET status = 'Cancelled' WHERE id = {0}", [s.Order.Id], Ct);

        await Assert.ThrowsAsync<ConflictException>(() => DeleteAsync(s, id, 2));
        await DeleteAsync(s, id, 1);

        await using var verify = Context(s.CompanyId);
        var saved = await verify.Set<SalesReturn>().IgnoreQueryFilters([ErpQueryFilters.SoftDelete]).AsNoTracking()
            .Include(x => x.Lines).ThenInclude(x => x.InvoiceAllocations).SingleAsync(x => x.Id == id, Ct);
        Assert.Equal((true, (DateTime?)EditNow, 2, SalesReturnStatus.Draft),
            (saved.IsDeleted, saved.DeletedAt, saved.Version, saved.Status));
        Assert.Single(Assert.Single(saved.Lines).InvoiceAllocations);
        Assert.False(await verify.Set<SalesReturn>().AnyAsync(Ct));

        Assert.Equal("SAL.RETURN.NOT_FOUND",
            (await Assert.ThrowsAsync<NotFoundException>(() => DeleteAsync(s, id, 2))).ErrorCode);
        await Assert.ThrowsAsync<NotFoundException>(() => UpdateAsync(s, id, 2, input));
        Assert.Equal("SAL.RETURN.REQUEST_CONFLICT",
            (await Assert.ThrowsAsync<ConflictException>(() => CreateAsync(s, input))).ErrorCode);
    }

    [Fact]
    public async Task FailedEditRollsBackAndTheReturnRowLockSerializesEditors()
    {
        // SD-RETURN-EDIT-005: a failure inside the guarded save keeps the old lines, allocations and version; an
        // editor waits for the return-row lock while another return of the same company proceeds.
        var s = await SeedAsync("EG");
        var created = await CreateAsync(s, Line("G-1", s.DeliveryA, 0.5m, (s.InvoiceLineA, 0.5m)));
        var id = created.Return.Id;
        await Assert.ThrowsAsync<InvalidOperationException>(() => UpdateAsync(s, id, 1,
            Line("G-1", s.DeliveryB, 1m), new RecordingGuard { AfterSave = () => throw new InvalidOperationException("audit") }));
        await AssertUnchangedAsync(s, id, s.DeliveryA, 1m);
        await using (var verify = Context(s.CompanyId))
            Assert.Single(await verify.Set<SalesReturnInvoiceAllocation>().ToListAsync(Ct));

        var other = await CreateAsync(s, Line("G-2", s.DeliveryB, 1m));
        await using var holder = Context(s.CompanyId);
        await using (var transaction = await holder.Database.BeginTransactionAsync(Ct))
        {
            await holder.Database.SqlQuery<long>($"""
                SELECT id AS [Value] FROM dbo.sal_return WITH (UPDLOCK, HOLDLOCK) WHERE id = {id}
                """).ToListAsync(Ct);
            Assert.Equal("blocked", await TryUpdateAsync(s, id, Line("G-1", s.DeliveryA, 0.25m)));
            Assert.Equal("updated", await TryUpdateAsync(s, other.Return.Id, Line("G-2", s.DeliveryB, 0.5m)));
            await transaction.RollbackAsync(Ct);
        }
        Assert.Equal("updated", await TryUpdateAsync(s, id, Line("G-1", s.DeliveryA, 0.25m)));
    }

    private async Task AssertUnchangedAsync(Seeded s, long id, long deliveryLineId, decimal quantity)
    {
        await using var verify = Context(s.CompanyId);
        var saved = await verify.Set<SalesReturn>().IgnoreQueryFilters([ErpQueryFilters.SoftDelete]).AsNoTracking()
            .Include(x => x.Lines).SingleAsync(x => x.Id == id, Ct);
        var line = Assert.Single(saved.Lines);
        Assert.Equal((1, Day.AddDays(1), deliveryLineId, quantity, false),
            (saved.Version, saved.BusinessDate, line.DeliveryLineId, line.Quantity, saved.IsDeleted));
    }

    private async Task<SalesReturn> UpdateAsync(Seeded s, long id, int version, SalesReturnDraftInput input,
        RecordingGuard? guard = null, bool allow = true, long userId = 7, DataScope? scope = null)
    {
        await using var db = Context(s.CompanyId);
        return await CommandService(db, s.CompanyId, guard ?? new RecordingGuard(), allow, userId, scope)
            .UpdateAsync(id, version, input, Ct);
    }

    private async Task DeleteAsync(Seeded s, long id, int version, bool allow = true, long userId = 7,
        DataScope? scope = null)
    {
        await using var db = Context(s.CompanyId);
        await CommandService(db, s.CompanyId, new RecordingGuard(), allow, userId, scope).DeleteAsync(id, version, Ct);
    }

    private async Task<string> TryUpdateAsync(Seeded s, long id, SalesReturnDraftInput input)
    {
        await using var db = Context(s.CompanyId);
        await db.Database.OpenConnectionAsync(Ct);
        await db.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT 2000", Ct);
        try
        {
            var version = await db.Set<SalesReturn>().Where(x => x.Id == id).Select(x => x.Version).SingleAsync(Ct);
            await CommandService(db, s.CompanyId, new RecordingGuard()).UpdateAsync(id, version, input, Ct);
            return "updated";
        }
        catch (Exception error) when (IsLockTimeout(error))
        {
            return "blocked";
        }
    }

    private static SalesReturnDraftCommandService CommandService(MyErpDbContext db, long companyId,
        IInventoryReferenceGuard guard, bool allow = true, long userId = 7, DataScope? scope = null)
    {
        var auth = new Mock<ICurrentAuthorization>();
        auth.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            new UserCompanyAuthorization(userId, companyId, null, null, allow && scope is null,
                allow && scope is { } value
                    ? new Dictionary<string, DataScope>
                    {
                        ["SAL.RETURN.EDIT"] = value, ["SAL.RETURN.DELETE"] = value
                    }
                    : new Dictionary<string, DataScope>()));
        var company = new SalesCompanyAccess(new FixedCompany(companyId));
        return new SalesReturnDraftCommandService(db, new SalesActionAccess(company, auth.Object), company,
            new SalesReturnDraftBuilder(db, guard), new FixedClock(EditNow));
    }
}
