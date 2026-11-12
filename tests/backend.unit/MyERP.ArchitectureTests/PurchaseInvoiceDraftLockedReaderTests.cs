using MyERP.Modules.Purchasing.Application;
using MyERP.Modules.Purchasing.Infrastructure;

namespace MyERP.ArchitectureTests;

public sealed class PurchaseInvoiceDraftLockedReaderTests
{
    [Fact]
    public async Task ReadsOrderThenReceiptsThenPricesWithStableDistinctIds()
    {
        var reader = new RecordingSourceReader();
        await PurchaseInvoiceDraftSourceSequence.ReadAsync(reader, 3, 5, 7,
            [19, 11], CancellationToken.None);
        reader.Events.Should().Equal("order:3:5:7", "receipts:11,19", "prices:11,19");
    }

    [Theory]
    [InlineData(0, 5, 7, new long[] { 11 })]
    [InlineData(3, 0, 7, new long[] { 11 })]
    [InlineData(3, 5, 0, new long[] { 11 })]
    [InlineData(3, 5, 7, new long[] { })]
    [InlineData(3, 5, 7, new long[] { 11, 11 })]
    [InlineData(3, 5, 7, new long[] { -1 })]
    public async Task InvalidScopeNeverStartsAuthorityRead(long company, long supplier,
        long order, long[] ids)
    {
        var reader = new RecordingSourceReader();
        Func<Task> act = () => PurchaseInvoiceDraftSourceSequence.ReadAsync(reader,
            company, supplier, order, ids, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
        reader.Events.Should().BeEmpty();
    }

    private sealed class RecordingSourceReader : IPurchaseInvoiceDraftSourceReader
    {
        public List<string> Events { get; } = [];

        public Task<PurchaseInvoiceOrderIdentity> ReadOrderAsync(long companyId,
            long supplierId, long orderId, CancellationToken cancellationToken)
        {
            Events.Add($"order:{companyId}:{supplierId}:{orderId}");
            return Task.FromResult(new PurchaseInvoiceOrderIdentity(companyId, supplierId,
                orderId, "TWD", "Approved", false));
        }

        public Task<IReadOnlyList<PurchaseInvoiceReceiptIdentity>> ReadReceiptsAsync(
            long companyId, IReadOnlyList<long> receiptLineIds, CancellationToken cancellationToken)
        {
            Events.Add($"receipts:{string.Join(',', receiptLineIds)}");
            return Task.FromResult<IReadOnlyList<PurchaseInvoiceReceiptIdentity>>([]);
        }

        public Task<IReadOnlyList<PurchaseInvoiceOrderLinePrice>> ReadPricesAsync(
            long companyId, IReadOnlyList<long> receiptLineIds, CancellationToken cancellationToken)
        {
            Events.Add($"prices:{string.Join(',', receiptLineIds)}");
            return Task.FromResult<IReadOnlyList<PurchaseInvoiceOrderLinePrice>>([]);
        }
    }
}
