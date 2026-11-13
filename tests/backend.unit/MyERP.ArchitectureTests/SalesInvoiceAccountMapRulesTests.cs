using MyERP.Modules.Finance.Application;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

/// <summary>FI-SALES-MAP-U01～U04（WU-FI-002a4b，BR-FI-147）：映射維護的純規則。</summary>
public sealed class SalesInvoiceAccountMapRulesTests
{
    private static AccountInfo Account(bool active = true, bool postable = true) =>
        new(1, 9, 1, "1131", "Receivable", "1", AccountType.Asset, BalanceDirection.Debit, active, postable);

    [Fact]
    public void InputIsTrimmedAndKeptWhenValid()
    {
        // FI-SALES-MAP-U01
        var input = SalesInvoiceAccountMapRules.Normalize(new SalesInvoiceAccountMapInput(3, " 1131", "4101 ", "2141"));
        input.Should().Be(new SalesInvoiceAccountMapInput(3, "1131", "4101", "2141"));
    }

    [Fact]
    public void BlankTooLongAndNegativeVersionReportEveryField()
    {
        // FI-SALES-MAP-U02
        var act = () => SalesInvoiceAccountMapRules.Normalize(
            new SalesInvoiceAccountMapInput(-1, null, " ", new string('1', 31)));
        var error = act.Should().Throw<RequestValidationException>().Which;
        error.Errors.Keys.Should().BeEquivalentTo(
            ["version", "receivableAccountCode", "revenueAccountCode", "outputTaxAccountCode"]);
        // 30 個字元是上限（與 MD 科目代碼同長）。
        SalesInvoiceAccountMapRules.Normalize(new SalesInvoiceAccountMapInput(0, new string('1', 30), "4", "2"))
            .ReceivableAccountCode.Should().HaveLength(30);
    }

    [Fact]
    public void AccountReasonFollowsMasterDataPostability()
    {
        // FI-SALES-MAP-U03：與 IChartOfAccounts.RequirePostableAsync 相同的錯誤碼。
        SalesInvoiceAccountMapRules.AccountReason(Account()).Should().BeNull();
        SalesInvoiceAccountMapRules.AccountReason(null).Should().Be("MD.ACCOUNT.NOT_FOUND");
        SalesInvoiceAccountMapRules.AccountReason(Account(active: false, postable: false)).Should().Be("MD.ACCOUNT.INACTIVE");
        SalesInvoiceAccountMapRules.AccountReason(Account(postable: false)).Should().Be("MD.ACCOUNT.NOT_POSTABLE");
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(2, 2, true)]
    [InlineData(0, 1, false)]
    [InlineData(1, 0, false)]
    [InlineData(1, 2, false)]
    [InlineData(3, 2, false)]
    public void VersionMustMatchSavedRowOrZeroWhenMissing(int requested, int saved, bool matches)
    {
        // FI-SALES-MAP-U04：0 表示「預期尚未設定」；已有列時必須送目前版本。
        SalesInvoiceAccountMapRules.VersionMatches(requested, saved).Should().Be(matches);
    }
}
