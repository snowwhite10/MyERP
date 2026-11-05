using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Domain;

namespace MyERP.ArchitectureTests;

public sealed class GlJournalLineDraftRulesTests
{
    private static GlJournalLineCandidate Leg(long accountId, string code, decimal debit, decimal credit,
        long companyId = 3, int mappingVersion = 4) =>
        new(new(GlAccountSnapshotStatus.Allowed,
            new GlAccountSnapshot(companyId, accountId, code, "Account " + code, 2, mappingVersion)), debit, credit);

    [Fact]
    public void BuildsOrderedCompleteDraftWithAccountAndMappingSnapshots()
    {
        var result = GlJournalLineDraftRules.Build(3, 4,
            [Leg(7, "1002", 12.5m, 0), Leg(8, "2001", 0, 12.5m)]);

        Assert.Equal(GlJournalLineDraftStatus.Allowed, result.Status);
        Assert.Equal(12.5m, result.TotalDebit);
        Assert.Equal(12.5m, result.TotalCredit);
        Assert.Equal(1, result.Lines[0].Sequence);
        Assert.Equal(2, result.Lines[1].Sequence);
        Assert.Equal(new GlJournalLineDraft(1, 7, "1002", "Account 1002", 2, 4, 12.5m, 0), result.Lines[0]);
        Assert.Equal(new GlJournalLineDraft(2, 8, "2001", "Account 2001", 2, 4, 0, 12.5m), result.Lines[1]);
    }

    [Fact]
    public void RejectsAnyFailedAccountWithoutReturningPartialLines()
    {
        var result = GlJournalLineDraftRules.Build(3, 4,
            [Leg(7, "1002", 12m, 0), new(new(GlAccountSnapshotStatus.Inactive), 0, 12m)]);

        Assert.Equal(GlJournalLineDraftStatus.InvalidAccountSnapshot, result.Status);
        Assert.Equal(2, result.InvalidLine);
        Assert.Empty(result.Lines);
        Assert.Equal(0, result.TotalDebit);
    }

    [Fact]
    public void RejectsWrongCompanyOrChangedMappingBeforeBuildingLines()
    {
        var foreign = GlJournalLineDraftRules.Build(3, 4,
            [Leg(7, "1002", 12m, 0), Leg(8, "2001", 0, 12m, companyId: 9)]);
        Assert.Equal(GlJournalLineDraftStatus.WrongCompany, foreign.Status);
        Assert.Equal(2, foreign.InvalidLine);
        Assert.Empty(foreign.Lines);

        var changed = GlJournalLineDraftRules.Build(3, 4,
            [Leg(7, "1002", 12m, 0), Leg(8, "2001", 0, 12m, mappingVersion: 5)]);
        Assert.Equal(GlJournalLineDraftStatus.MappingChanged, changed.Status);
        Assert.Equal(2, changed.InvalidLine);
        Assert.Empty(changed.Lines);
    }

    [Fact]
    public void RejectsMalformedSnapshotAndUnbalancedAmountsWithoutPartialDraft()
    {
        var malformed = GlJournalLineDraftRules.Build(3, 4,
            [Leg(7, "1002", 12m, 0),
                new(new(GlAccountSnapshotStatus.Allowed, new GlAccountSnapshot(3, 8, "2001", "", 2, 4)), 0, 12m)]);
        Assert.Equal(GlJournalLineDraftStatus.InvalidAccountSnapshot, malformed.Status);
        Assert.Empty(malformed.Lines);

        var unbalanced = GlJournalLineDraftRules.Build(3, 4,
            [Leg(7, "1002", 12m, 0), Leg(8, "2001", 0, 11m)]);
        Assert.Equal(GlJournalLineDraftStatus.InvalidBalance, unbalanced.Status);
        Assert.Equal(GlJournalBalanceStatus.Unbalanced, unbalanced.BalanceStatus);
        Assert.Empty(unbalanced.Lines);
    }

    [Fact]
    public void RejectsEmptyOrInvalidInput()
    {
        Assert.Equal(GlJournalLineDraftStatus.InvalidInput, GlJournalLineDraftRules.Build(0, 4, []).Status);
        Assert.Equal(GlJournalLineDraftStatus.InvalidInput, GlJournalLineDraftRules.Build(3, 0, []).Status);
        Assert.Equal(GlJournalLineDraftStatus.InvalidBalance, GlJournalLineDraftRules.Build(3, 4, []).Status);
    }

    [Fact]
    public void PreservesRepeatedAccountLegsAndReportsInvalidAmountLine()
    {
        var repeated = GlJournalLineDraftRules.Build(3, 4,
            [Leg(7, "1002", 8m, 0), Leg(7, "1002", 0, 8m)]);
        Assert.Equal(GlJournalLineDraftStatus.Allowed, repeated.Status);
        Assert.Equal(2, repeated.Lines.Count);

        var badAmount = GlJournalLineDraftRules.Build(3, 4,
            [Leg(7, "1002", 8m, 0), Leg(8, "2001", 0, 8.00001m)]);
        Assert.Equal(GlJournalLineDraftStatus.InvalidBalance, badAmount.Status);
        Assert.Equal(GlJournalBalanceStatus.InvalidPrecision, badAmount.BalanceStatus);
        Assert.Equal(2, badAmount.InvalidLine);
        Assert.Empty(badAmount.Lines);
    }

    [Fact]
    public void RejectsContradictorySnapshotsForRepeatedAccountWithoutPartialLines()
    {
        var first = Leg(7, "1002", 8m, 0);
        var variants = new[]
        {
            new GlAccountSnapshot(3, 7, "1002", "Renamed", 2, 4),
            new GlAccountSnapshot(3, 7, "1002", "Account 1002", 3, 4),
            new GlAccountSnapshot(3, 7, "1003", "Account 1002", 2, 4),
            new GlAccountSnapshot(3, 8, "1002", "Account 1002", 2, 4)
        };

        foreach (var snapshot in variants)
        {
            var result = GlJournalLineDraftRules.Build(3, 4,
                [first, new(new(GlAccountSnapshotStatus.Allowed, snapshot), 0, 8m)]);
            Assert.Equal(GlJournalLineDraftStatus.SnapshotConflict, result.Status);
            Assert.Equal(2, result.InvalidLine);
            Assert.Empty(result.Lines);
            Assert.Equal(0, result.TotalDebit);
            Assert.Equal(0, result.TotalCredit);
        }
    }
}
