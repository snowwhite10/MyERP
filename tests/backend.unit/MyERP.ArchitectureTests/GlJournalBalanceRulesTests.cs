using MyERP.Modules.Finance.Domain;

namespace MyERP.ArchitectureTests;

public sealed class GlJournalBalanceRulesTests
{
    [Fact]
    public void BalancedLegsKeepExactTotalsAndRepeatedAccounts()
    {
        var result = GlJournalBalanceRules.Validate([
            new(17, 10.1250m, 0), new(18, 0, 4.1250m), new(18, 0, 6m)]);
        Assert.Equal(GlJournalBalanceStatus.Allowed, result.Status);
        Assert.Equal(10.1250m, result.TotalDebit);
        Assert.Equal(10.1250m, result.TotalCredit);
    }

    [Fact]
    public void RequiresAtLeastTwoLinesAndPositiveAccountIdentity()
    {
        Assert.Equal(GlJournalBalanceStatus.TooFewLines, GlJournalBalanceRules.Validate(null).Status);
        Assert.Equal(GlJournalBalanceStatus.TooFewLines, GlJournalBalanceRules.Validate([]).Status);
        Assert.Equal(GlJournalBalanceStatus.TooFewLines,
            GlJournalBalanceRules.Validate([new(1, 1m, 0)]).Status);
        var invalid = GlJournalBalanceRules.Validate([new(1, 1m, 0), new(0, 0, 1m)]);
        Assert.Equal(GlJournalBalanceStatus.InvalidAccount, invalid.Status);
        Assert.Equal(2, invalid.InvalidLine);
    }

    [Theory]
    [InlineData("0", "0", GlJournalBalanceStatus.InvalidSide)]
    [InlineData("-1", "0", GlJournalBalanceStatus.InvalidSide)]
    [InlineData("1", "1", GlJournalBalanceStatus.InvalidSide)]
    [InlineData("0.00001", "0", GlJournalBalanceStatus.InvalidPrecision)]
    [InlineData("1000000000000000", "0", GlJournalBalanceStatus.Overflow)]
    public void RejectsInvalidLegWithoutPartialTotals(string debitText, string creditText, GlJournalBalanceStatus status)
    {
        var debit = decimal.Parse(debitText, System.Globalization.CultureInfo.InvariantCulture);
        var credit = decimal.Parse(creditText, System.Globalization.CultureInfo.InvariantCulture);
        var result = GlJournalBalanceRules.Validate([new(1, 1m, 0), new(2, debit, credit)]);
        Assert.Equal(status, result.Status);
        Assert.Equal(2, result.InvalidLine);
        Assert.Equal(0, result.TotalDebit);
        Assert.Equal(0, result.TotalCredit);
    }

    [Fact]
    public void AcceptsSqlMaximumButRejectsAggregateOverflowAndImbalance()
    {
        const decimal maximum = 999999999999999.9999m;
        Assert.Equal(GlJournalBalanceStatus.Allowed, GlJournalBalanceRules.Validate([
            new(1, maximum, 0), new(2, 0, maximum)]).Status);
        Assert.Equal(GlJournalBalanceStatus.Overflow, GlJournalBalanceRules.Validate([
            new(1, maximum, 0), new(2, 0, maximum), new(3, 0.0001m, 0)]).Status);
        var unbalanced = GlJournalBalanceRules.Validate([new(1, 1m, 0), new(2, 0, 0.9999m)]);
        Assert.Equal(GlJournalBalanceStatus.Unbalanced, unbalanced.Status);
        Assert.Equal(0, unbalanced.TotalDebit);
    }
}
