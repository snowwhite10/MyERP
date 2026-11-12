using MyERP.Modules.Purchasing.Infrastructure;

namespace MyERP.ArchitectureTests;

public sealed class PurchaseInvoiceDraftSqlSessionTests
{
    [Fact]
    public void LockResourceIsStableAndScopedByCompanyAndSupplier()
    {
        PurchaseInvoiceDraftSqlSession.LockResource(5, 7).Should().Be("pur_invoice_draft:5:7");
        PurchaseInvoiceDraftSqlSession.LockResource(5, 8).Should().NotBe(
            PurchaseInvoiceDraftSqlSession.LockResource(5, 7));
        PurchaseInvoiceDraftSqlSession.LockResource(6, 7).Should().NotBe(
            PurchaseInvoiceDraftSqlSession.LockResource(5, 7));
    }

    [Theory]
    [InlineData(0, 7)]
    [InlineData(5, 0)]
    [InlineData(-1, 7)]
    public void InvalidScopeCannotAcquireLock(long companyId, long supplierId)
    {
        Action act = () => PurchaseInvoiceDraftSqlSession.LockResource(companyId, supplierId);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
