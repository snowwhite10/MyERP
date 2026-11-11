using MyERP.Modules.Purchasing.Application;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

public sealed class PurchaseInvoiceSnapshotFingerprintTests
{
    private static readonly DateOnly BusinessDate = new(2026, 11, 11);
    private static PurchaseInvoiceMatchLine Line(int no, long receiptId, decimal net = 2m) =>
        new(receiptId, new(no, 1m, 2m, 0m, 2m, 0m, net, 0m, net));

    private static string Hash(IReadOnlyList<PurchaseInvoiceMatchLine> lines,
        IReadOnlyList<PurchaseInvoiceResolvedLine>? evidence = null,
        long companyId = 1, long invoiceId = 7, long supplierId = 3,
        string currency = "TWD", DateOnly? date = null) =>
        PurchaseInvoiceSnapshotFingerprint.Create(companyId, invoiceId, supplierId,
            currency, date ?? BusinessDate, lines, evidence ?? []);

    [Fact]
    public void SameSavedSnapshotIsStableAcrossLineAndEvidenceEnumerationOrder()
    {
        var difference = new PurchaseInvoiceAmountDifference("NET", 2m, 2.0001m, .0001m);
        var gross = new PurchaseInvoiceAmountDifference("GROSS", 2m, 2.0001m, .0001m);
        var first = new PurchaseInvoiceResolvedLine(1, "Accept", "approved", [difference, gross]);
        var second = new PurchaseInvoiceResolvedLine(2, "Accept", "reviewed", [difference, gross]);
        var a = Hash([Line(2, 20, 2.0001m), Line(1, 10, 2.0001m)], [second, first]);
        var b = Hash([Line(1, 10, 2.0001m), Line(2, 20, 2.0001m)], [first, second]);
        a.Should().Be(b).And.StartWith("PUR-INVOICE-POST-v1:");
        a.Length.Should().Be("PUR-INVOICE-POST-v1:".Length + 64);
    }

    [Fact]
    public void IdentityDateLineAndEvidenceChangesAlterFingerprint()
    {
        var original = Hash([Line(1, 10)]);
        Hash([Line(1, 10)], companyId: 2).Should().NotBe(original);
        Hash([Line(1, 10)], invoiceId: 8).Should().NotBe(original);
        Hash([Line(1, 10)], supplierId: 4).Should().NotBe(original);
        Hash([Line(1, 10)], currency: "USD").Should().NotBe(original);
        Hash([Line(1, 10)], date: BusinessDate.AddDays(1)).Should().NotBe(original);
        Hash([Line(2, 10)]).Should().NotBe(original);
        Hash([Line(1, 11)]).Should().NotBe(original);
        Hash([Line(1, 10, 2.0001m)],
            [new(1, "Accept", "approved", [new("NET", 2m, 2.0001m, .0001m),
                new("GROSS", 2m, 2.0001m, .0001m)])]).Should().NotBe(original);
    }

    [Fact]
    public void EquivalentDecimalScalesHaveSameFingerprint()
    {
        var a = Hash([Line(1, 10, 2m)]);
        var b = Hash([new(10, new(1, 1.000000m, 2.000000m, 0.0000m,
            2.000000m, 0.0000m, 2.0000m, 0.0000m, 2.0000m))]);
        a.Should().Be(b);
    }

    [Fact]
    public void ChangedAcceptedReasonChangesFingerprint()
    {
        var differences = new PurchaseInvoiceAmountDifference[] {
            new("NET", 2m, 2.0001m, .0001m), new("GROSS", 2m, 2.0001m, .0001m) };
        var a = Hash([Line(1, 10, 2.0001m)], [new(1, "Accept", "first", differences)]);
        var b = Hash([Line(1, 10, 2.0001m)], [new(1, "Accept", "second", differences)]);
        a.Should().NotBe(b);
    }

    [Fact]
    public void EverySavedPriceTaxAndQuantityFieldAffectsFingerprint()
    {
        var original = Hash([Line(1, 10)]);
        PurchaseInvoiceMatchLine Changed(decimal quantity = 1m, decimal orderPrice = 2m,
            decimal orderRate = 0m, decimal invoicePrice = 2m, decimal invoiceRate = 0m,
            decimal net = 2m, decimal tax = 0m, decimal gross = 2m) =>
            new(10, new(1, quantity, orderPrice, orderRate, invoicePrice, invoiceRate,
                net, tax, gross));

        Hash([Changed(quantity: 2m, net: 4m, gross: 4m)]).Should().NotBe(original);
        Hash([Changed(orderPrice: 3m, invoicePrice: 3m, net: 3m, gross: 3m)]).Should().NotBe(original);
        Hash([Changed(orderRate: 10m, invoiceRate: 10m, tax: .2m, gross: 2.2m)]).Should().NotBe(original);
        Hash([Changed(invoicePrice: 2.000001m)],
            [new(1, "Accept", "ok", [new("UNIT_PRICE", 2m, 2.000001m, .000001m)])])
            .Should().NotBe(original);
        Hash([Changed(invoiceRate: 1m)],
            [new(1, "Accept", "ok", [new("TAX_RATE", 0m, 1m, 1m)])])
            .Should().NotBe(original);
        Hash([Changed(net: 2.0001m, gross: 2.0001m)],
            [new(1, "Accept", "ok", [new("NET", 2m, 2.0001m, .0001m),
                new("GROSS", 2m, 2.0001m, .0001m)])]).Should().NotBe(original);
        Hash([Changed(tax: .0001m, gross: 2.0001m)],
            [new(1, "Accept", "ok", [new("TAX", 0m, .0001m, .0001m),
                new("GROSS", 2m, 2.0001m, .0001m)])]).Should().NotBe(original);
    }

    [Fact]
    public void InvalidIdentityLinesOrEvidenceRejectWithoutHash()
    {
        Action noCompany = () => Hash([Line(1, 10)], companyId: 0);
        Action badCurrency = () => Hash([Line(1, 10)], currency: "usd");
        Action duplicateLine = () => Hash([Line(1, 10), Line(1, 20)]);
        Action staleEvidence = () => Hash([Line(1, 10, 2.0001m)], []);
        Action tooMany = () => Hash(Enumerable.Range(1, 1001).Select(i => Line(i, i)).ToArray());
        noCompany.Should().Throw<BusinessRuleException>();
        badCurrency.Should().Throw<BusinessRuleException>();
        duplicateLine.Should().Throw<BusinessRuleException>();
        staleEvidence.Should().Throw<BusinessRuleException>();
        tooMany.Should().Throw<BusinessRuleException>();
    }
}
