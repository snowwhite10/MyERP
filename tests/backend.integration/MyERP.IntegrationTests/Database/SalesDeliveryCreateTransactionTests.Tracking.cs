using Microsoft.EntityFrameworkCore;
using Moq;
using MyERP.Modules.Sales;
using MyERP.Modules.Sales.Application;
using MyERP.Modules.Sales.Domain;
using MyERP.Modules.System.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests.Database;

/// <summary>WU-SD-004a2b14 (ADR-140): SD-DEL-016～019 — saved lot/serial selections on real SQL.</summary>
public sealed partial class SalesDeliveryCreateTransactionTests
{
    // Line A (item 31, ordered 5) is serial tracked; line B (item 32, ordered 1.5) is lot tracked.
    private static RecordingGuard TrackingGuard() => new() { SerialItem = 31, TrackedItem = 32 };

    [Fact]
    public async Task DraftSavesTrimmedSelectionsReplaceSwapsThemAndDetailReturnsThem()
    {
        var (companyId, order) = await SeedAsync("TA", SalesOrderStatus.Approved);
        long lineA = order.Lines[0].Id, lineB = order.Lines[1].Id;
        await using var db = Context(companyId);

        // A Draft may be incomplete (lot 1.25 of 1.5); submission requires the full quantity.
        var saved = await Service(db, companyId, TrackingGuard()).CreateAsync(new SalesDeliveryDraftInput(order.Id, Day,
        [
            new SalesDeliveryLineInput(1, lineA, Warehouse, Location, 2m,
                [new SalesDeliveryTrackingInput(null, " sn-1 ", 1m), new SalesDeliveryTrackingInput(null, "SN-2", 1m)]),
            new SalesDeliveryLineInput(2, lineB, Warehouse, null, 1.5m,
                [new SalesDeliveryTrackingInput("LOT-A", null, 1m), new SalesDeliveryTrackingInput(" LOT-B", null, 0.25m)])
        ]), Ct);

        await using (var verify = Context(companyId))
        {
            var rows = await verify.Set<SalesDeliveryLineTracking>().AsNoTracking()
                .OrderBy(x => x.LineId).ThenBy(x => x.SeqNo).ToListAsync(Ct);
            Assert.Equal(
                [(1, null, "sn-1", 1m), (2, null, "SN-2", 1m), (1, "LOT-A", null, 1m), (2, "LOT-B", null, 0.25m)],
                rows.Select(x => (x.SeqNo, x.LotNo, x.SerialNo, x.Quantity)));
            Assert.All(rows, x => Assert.Equal(companyId, x.CompanyId));
        }

        var replaced = await Commands(db, companyId, TrackingGuard()).ReplaceAsync(saved.Id, 1,
            new SalesDeliveryDraftInput(order.Id, Day,
                [new SalesDeliveryLineInput(1, lineA, Warehouse, Location, 1m, [new SalesDeliveryTrackingInput(null, "SN-3", 1m)])]),
            Ct);
        Assert.Equal(2, replaced.Version);
        Assert.Equal(["SN-3"], replaced.Lines.Single().Tracking.Select(x => x.SerialNo));

        await using var read = Context(companyId);
        // Old selections leave with their replaced lines (cascade); no orphan rows remain.
        Assert.Equal(1, await read.Set<SalesDeliveryLineTracking>().CountAsync(Ct));
        var detail = await Queries(read, companyId).GetAsync(saved.Id, Ct);
        var line = Assert.Single(detail.Lines);
        Assert.Equal([(1, (string?)null, (string?)"SN-3", 1m)],
            line.Tracking.Select(x => (x.SeqNo, x.LotNo, x.SerialNo, x.Quantity)));
    }

    [Fact]
    public async Task TrackingModeAndDuplicateSerialFailuresNameTheLineAndRollBack()
    {
        var (companyId, order) = await SeedAsync("TB", SalesOrderStatus.Approved);
        long lineA = order.Lines[0].Id, lineB = order.Lines[1].Id;
        await using var db = Context(companyId);

        // The same serial of one item cannot leave twice in one delivery (case-insensitive, trimmed).
        var duplicate = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Service(db, companyId, TrackingGuard()).CreateAsync(new SalesDeliveryDraftInput(order.Id, Day,
            [
                new SalesDeliveryLineInput(1, lineA, Warehouse, null, 1m, [new SalesDeliveryTrackingInput(null, "SN-1", 1m)]),
                new SalesDeliveryLineInput(3, lineA, Warehouse, Location, 1m, [new SalesDeliveryTrackingInput(null, " sn-1", 1m)])
            ]), Ct));
        Assert.Equal(("SAL.DELIVERY.SERIAL_DUPLICATE", 3), (duplicate.ErrorCode, duplicate.Extensions!["lineNo"]));

        // An untracked item cannot carry selections.
        var untracked = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Service(db, companyId).CreateAsync(new SalesDeliveryDraftInput(order.Id, Day,
                [new SalesDeliveryLineInput(2, lineB, Warehouse, null, 1m, [new SalesDeliveryTrackingInput("LOT-A", null, 1m)])]),
                Ct));
        Assert.Equal(("SAL.DELIVERY.TRACKING_MODE_MISMATCH", 2), (untracked.ErrorCode, untracked.Extensions!["lineNo"]));

        // A serial-tracked line must be a whole number of units, even before selections are entered.
        var fraction = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Service(db, companyId, TrackingGuard()).CreateAsync(Input(order.Id, lineA, 1.5m, lineNo: 5), Ct));
        Assert.Equal(("SAL.DELIVERY.TRACKING_MODE_MISMATCH", 5), (fraction.ErrorCode, fraction.Extensions!["lineNo"]));

        // A lot-tracked line cannot take serial numbers.
        var lotWithSerial = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Service(db, companyId, TrackingGuard()).CreateAsync(new SalesDeliveryDraftInput(order.Id, Day,
                [new SalesDeliveryLineInput(6, lineB, Warehouse, null, 1m, [new SalesDeliveryTrackingInput(null, "SN-9", 1m)])]),
                Ct));
        Assert.Equal(("SAL.DELIVERY.TRACKING_MODE_MISMATCH", 6), (lotWithSerial.ErrorCode, lotWithSerial.Extensions!["lineNo"]));

        await using var verify = Context(companyId);
        Assert.False(await verify.Set<SalesDelivery>().AnyAsync(Ct));
        Assert.False(await verify.Set<SalesDeliveryLineTracking>().AnyAsync(Ct));
        Assert.False(await verify.Set<NumberSequence>().AnyAsync(Ct));
    }

    [Fact]
    public async Task SubmitRequiresSelectionsTotallingEachTrackedLine()
    {
        var (companyId, order) = await SeedAsync("TC", SalesOrderStatus.Approved);
        long lineA = order.Lines[0].Id, lineB = order.Lines[1].Id;
        await using var db = Context(companyId);
        var draft = await Service(db, companyId, TrackingGuard()).CreateAsync(new SalesDeliveryDraftInput(order.Id, Day,
        [
            new SalesDeliveryLineInput(1, lineB, Warehouse, null, 1.5m, [new SalesDeliveryTrackingInput("LOT-A", null, 1.5m)]),
            new SalesDeliveryLineInput(2, lineA, Warehouse, null, 2m, [new SalesDeliveryTrackingInput(null, "SN-1", 1m)])
        ]), Ct);

        var missing = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Submission(db, companyId, new FakeWorkflow(), TrackingGuard()).SubmitAsync(draft.Id, 1, Ct));
        Assert.Equal(("SAL.DELIVERY.TRACKING_REQUIRED", 2), (missing.ErrorCode, missing.Extensions!["lineNo"]));
        await using (var verify = Context(companyId))
            Assert.Equal((SalesDeliveryStatus.Draft, 1), await verify.Set<SalesDelivery>()
                .Where(x => x.Id == draft.Id).Select(x => ValueTuple.Create(x.Status, x.Version)).SingleAsync(Ct));

        await Commands(db, companyId, TrackingGuard()).ReplaceAsync(draft.Id, 1, new SalesDeliveryDraftInput(order.Id, Day,
        [
            new SalesDeliveryLineInput(1, lineB, Warehouse, null, 1.5m, [new SalesDeliveryTrackingInput("LOT-A", null, 1.5m)]),
            new SalesDeliveryLineInput(2, lineA, Warehouse, null, 2m,
                [new SalesDeliveryTrackingInput(null, "SN-1", 1m), new SalesDeliveryTrackingInput(null, "SN-2", 1m)])
        ]), Ct);
        var submitted = await Submission(db, companyId, new FakeWorkflow(), TrackingGuard()).SubmitAsync(draft.Id, 2, Ct);
        Assert.Equal((SalesDeliveryStatus.Pending, 3), (submitted.Delivery.Status, submitted.Delivery.Version));
        Assert.Equal(3, submitted.Delivery.Lines.Sum(x => x.Tracking.Count));
    }

    private static SalesDeliveryQueryService Queries(MyErpDbContext db, long companyId)
    {
        var auth = new Mock<ICurrentAuthorization>();
        auth.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            new UserCompanyAuthorization(7, companyId, null, null, true, new Dictionary<string, DataScope>()));
        var company = new SalesCompanyAccess(new FixedCompany(companyId));
        return new SalesDeliveryQueryService(db,
            new SalesActionAccess(company, auth.Object), company);
    }
}
