using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Domain;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

/// <summary>FI-PUR-MAP-U01～U03（WU-FI-003a2c，BR-FI-150）：採購發票科目映射維護的純規則。</summary>
public sealed class PurchaseInvoiceAccountMapRulesTests
{
    private static PurchaseInvoiceAccountMapping Saved() => new()
    {
        PayableAccountCode = "2101",
        PurchaseAccountCode = "5111",
        InputTaxAccountCode = "1151"
    };

    [Fact]
    public void InputIsTrimmedAndKeptWhenValid()
    {
        // FI-PUR-MAP-U01
        var input = PurchaseInvoiceAccountMapRules.Normalize(new PurchaseInvoiceAccountMapInput(3, " 2101", "5111 ", "\t1151"));
        input.Should().Be(new PurchaseInvoiceAccountMapInput(3, "2101", "5111", "1151"));
    }

    [Fact]
    public void BlankTooLongAndNegativeVersionReportEveryField()
    {
        // FI-PUR-MAP-U02
        var act = () => PurchaseInvoiceAccountMapRules.Normalize(
            new PurchaseInvoiceAccountMapInput(-1, null, " ", new string('1', 31)));
        var error = act.Should().Throw<RequestValidationException>().Which;
        error.Errors.Keys.Should().BeEquivalentTo(
            ["version", "payableAccountCode", "purchaseAccountCode", "inputTaxAccountCode"]);
        PurchaseInvoiceAccountMapRules.Normalize(new PurchaseInvoiceAccountMapInput(0, new string('2', 30), "5", "1"))
            .PayableAccountCode.Should().HaveLength(PurchaseInvoiceAccountMapping.CodeMaxLength);
    }

    [Theory]
    [InlineData("2101", "5111", "1151", true)]
    [InlineData("2101", "5111", "1152", false)]
    [InlineData("2101", "6102", "1151", false)]
    [InlineData("2171", "5111", "1151", false)]
    [InlineData("2101", "5111", "1151 ", false)]
    public void SameAccountsComparesEveryCodeOrdinally(string payable, string purchase, string tax, bool same)
    {
        // FI-PUR-MAP-U03：零變更判斷（changed=false 不遞增版本）只在三個代碼完全相同時成立；Normalize 已先修剪。
        PurchaseInvoiceAccountMapRules.SameAccounts(Saved(), new PurchaseInvoiceAccountMapInput(1, payable, purchase, tax))
            .Should().Be(same);
    }
}
