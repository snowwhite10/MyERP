using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Domain;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

/// <summary>FI-COLL-MAP-U01～U05（WU-FI-005a2d，BR-FI-154，ADR-123）：收款科目映射維護的純規則。</summary>
public sealed class SalesCollectionAccountMapRulesTests
{
    private static SalesCollectionAccountMapping Saved() => new()
    {
        UnappliedCashAccountCode = "2181",
        ReceivableAccountCode = "1131"
    };

    [Fact]
    public void InputIsTrimmedAndKeptWhenValid()
    {
        // FI-COLL-MAP-U01
        SalesCollectionAccountMapRules.Normalize(new SalesCollectionAccountMapInput(3, " 2181", "1131\t"))
            .Should().Be(new SalesCollectionAccountMapInput(3, "2181", "1131"));
    }

    [Fact]
    public void BlankTooLongAndNegativeVersionReportEveryField()
    {
        // FI-COLL-MAP-U02
        var act = () => SalesCollectionAccountMapRules.Normalize(new SalesCollectionAccountMapInput(-1, " ", new string('1', 31)));
        act.Should().Throw<RequestValidationException>().Which.Errors.Keys
            .Should().BeEquivalentTo(["version", "unappliedCashAccountCode", "receivableAccountCode"]);
        var nulls = () => SalesCollectionAccountMapRules.Normalize(new SalesCollectionAccountMapInput(0, null, null));
        nulls.Should().Throw<RequestValidationException>().Which.Errors.Keys
            .Should().BeEquivalentTo(["unappliedCashAccountCode", "receivableAccountCode"]);
        SalesCollectionAccountMapRules.Normalize(new SalesCollectionAccountMapInput(0, new string('2', 30), "1"))
            .UnappliedCashAccountCode.Should().HaveLength(SalesCollectionAccountMapping.CodeMaxLength);
    }

    [Theory]
    [InlineData("2181", "1131", false)]
    [InlineData("2181", "2181", true)]
    [InlineData("ab12", "AB12", true)]
    [InlineData("2181", "21811", false)]
    public void SameCodeForBothAccountsIsCaseInsensitive(string unapplied, string receivable, bool collide)
    {
        // FI-COLL-MAP-U03（ADR-120）：與 SQL 檢查一致（修剪後不分大小寫）；相同時核銷會借貸同一科目，應收永不減少。
        SalesCollectionAccountMapRules.AccountsCollide(new SalesCollectionAccountMapInput(0, unapplied, receivable))
            .Should().Be(collide);
    }

    [Theory]
    [InlineData("2181", "1131", true)]
    [InlineData("2181", "1132", false)]
    [InlineData("2182", "1131", false)]
    [InlineData("2181", "1131 ", false)]
    public void SameAccountsComparesEveryCodeOrdinally(string unapplied, string receivable, bool same)
    {
        // FI-COLL-MAP-U04：零變更（changed=false 不遞增版本）只在兩個代碼完全相同時成立；Normalize 已先修剪。
        SalesCollectionAccountMapRules.SameAccounts(Saved(), new SalesCollectionAccountMapInput(1, unapplied, receivable))
            .Should().Be(same);
    }

    [Theory]
    [InlineData("1131", "1131", false)]
    [InlineData("1131", "1132", true)]
    [InlineData("ab1", "AB1", false)]
    [InlineData("1131", null, false)]
    public void ReceivableWarningOnlyWhenTheSalesInvoiceMapNamesAnotherAccount(string collection, string? salesInvoice,
        bool warn)
    {
        // FI-COLL-MAP-U05（ADR-120）：與 SD 發票映射應收科目不同只警告不阻擋；SD 映射未設定時不警告。
        SalesCollectionAccountMapRules.ReceivableDiffers(collection, salesInvoice).Should().Be(warn);
    }
}
