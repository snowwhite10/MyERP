using MyERP.Modules.Purchasing.Application;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

public sealed class PurchaseInvoiceNumberConflictTests
{
    [Fact]
    public void SameSupplierAndCompanyRejectEquivalentNumberIncludingSoftDeleted()
    {
        var rows = new[] { new PurchaseInvoiceNumberOwner(10, 20, 30, "AB-123", true) };
        Action act = () => PurchaseInvoiceNumberConflict.EnsureAvailable(10, 20, null, " ab-１２３ ", rows);
        act.Should().Throw<ConflictException>().Which.ErrorCode.Should().Be("PUR.INVOICE.NUMBER_EXISTS");
    }

    [Fact]
    public void CurrentInvoiceMayKeepItsNumberButAnotherOwnerStillConflicts()
    {
        var rows = new[] { new PurchaseInvoiceNumberOwner(10, 20, 30, "AB-123", false) };
        PurchaseInvoiceNumberConflict.EnsureAvailable(10, 20, 30, "ab-123", rows);
        Action act = () => PurchaseInvoiceNumberConflict.EnsureAvailable(10, 20, 31, "ab-123", rows);
        act.Should().Throw<ConflictException>().Which.ErrorCode.Should().Be("PUR.INVOICE.NUMBER_EXISTS");
    }

    [Fact]
    public void OtherCompanyOrSupplierCanUseSameNumber()
    {
        var rows = new[]
        {
            new PurchaseInvoiceNumberOwner(11, 20, 30, "AB-123", false),
            new PurchaseInvoiceNumberOwner(10, 21, 31, "AB-123", true)
        };
        PurchaseInvoiceNumberConflict.EnsureAvailable(10, 20, null, "ab-123", rows);
    }

    [Fact]
    public void InvalidCandidateAndDuplicateOwnerRowsFailClosed()
    {
        Action badNumber = () => PurchaseInvoiceNumberConflict.EnsureAvailable(10, 20, null, " ", []);
        badNumber.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("PUR.INVOICE.INVALID_NUMBER");

        var rows = new[]
        {
            new PurchaseInvoiceNumberOwner(10, 20, 30, "AB-123", false),
            new PurchaseInvoiceNumberOwner(10, 20, 30, "AB-123", true)
        };
        Action duplicateOwner = () => PurchaseInvoiceNumberConflict.EnsureAvailable(10, 20, 30, "ab-123", rows);
        duplicateOwner.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("PUR.INVOICE.INVALID_NUMBER_SNAPSHOT");
    }

    [Theory]
    [InlineData(0, 20, null)]
    [InlineData(10, 0, null)]
    [InlineData(10, 20, 0L)]
    public void InvalidScopeOrEditIdentityFailsClosed(long companyId, long supplierId, long? invoiceId)
    {
        Action act = () => PurchaseInvoiceNumberConflict.EnsureAvailable(companyId, supplierId, invoiceId, "AB-123", []);
        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("PUR.INVOICE.INVALID_NUMBER_SNAPSHOT");
    }

    [Fact]
    public void NoncanonicalStoredKeyFailsClosed()
    {
        var rows = new[] { new PurchaseInvoiceNumberOwner(10, 20, 30, "ab-123", false) };
        Action act = () => PurchaseInvoiceNumberConflict.EnsureAvailable(10, 20, null, "AB-123", rows);
        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("PUR.INVOICE.INVALID_NUMBER_SNAPSHOT");
    }
}
