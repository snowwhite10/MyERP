using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Domain;
using MyERP.Modules.MasterData.Contracts;

namespace MyERP.ArchitectureTests;

public sealed class GlJournalDraftRecheckRulesTests
{
    private static AccountInfo Account(long id, string code, string name, int version = 2,
        long companyId = 3, bool active = true, bool postable = true) =>
        new(id, companyId, version, code, name, null, AccountType.Asset,
            BalanceDirection.Debit, active, postable);

    private static GlJournalLineDraftResult Draft() => new(GlJournalLineDraftStatus.Allowed,
        [new(1, 7, "1002", "Cash", 2, 4, 8, 0),
         new(2, 8, "2001", "Payable", 2, 4, 0, 8)], 8, 8);

    [Fact]
    public void AcceptsOnlyMatchingFreshAccounts()
    {
        var result = GlJournalDraftRecheckRules.Validate(3, 4, Draft(), 4,
            [Account(7, "1002", "Cash"), Account(8, "2001", "Payable")]);
        Assert.Equal(GlJournalDraftRecheckStatus.Allowed, result.Status);
        Assert.Null(result.InvalidLine);
    }

    [Fact]
    public void RejectsChangedMappingBeforeInspectingAccounts()
    {
        var result = GlJournalDraftRecheckRules.Validate(3, 4, Draft(), 5, []);
        Assert.Equal(GlJournalDraftRecheckStatus.MappingChanged, result.Status);
        Assert.Null(result.InvalidLine);
    }

    [Theory]
    [InlineData("Renamed", 2)]
    [InlineData("Payable", 3)]
    public void RejectsChangedSnapshotWithoutAcceptingEarlierLines(string name, int version)
    {
        var result = GlJournalDraftRecheckRules.Validate(3, 4, Draft(), 4,
            [Account(7, "1002", "Cash"), Account(8, "2001", name, version)]);
        Assert.Equal(GlJournalDraftRecheckStatus.SnapshotChanged, result.Status);
        Assert.Equal(2, result.InvalidLine);
    }

    [Theory]
    [InlineData(false, true, GlAccountSnapshotStatus.Inactive)]
    [InlineData(true, false, GlAccountSnapshotStatus.NotPostable)]
    public void PreservesFreshAccountRejection(bool active, bool postable, GlAccountSnapshotStatus reason)
    {
        var result = GlJournalDraftRecheckRules.Validate(3, 4, Draft(), 4,
            [Account(7, "1002", "Cash"), Account(8, "2001", "Payable", active: active, postable: postable)]);
        Assert.Equal(GlJournalDraftRecheckStatus.AccountRejected, result.Status);
        Assert.Equal(2, result.InvalidLine);
        Assert.Equal(reason, result.AccountStatus);
    }

    [Fact]
    public void RejectsMissingForeignOrChangedIdentityAtItsLine()
    {
        foreach (var second in new AccountInfo?[]
        {
            null, Account(8, "2001", "Payable", companyId: 9), Account(9, "2001", "Payable")
        })
        {
            var result = GlJournalDraftRecheckRules.Validate(3, 4, Draft(), 4,
                [Account(7, "1002", "Cash"), second]);
            Assert.Equal(GlJournalDraftRecheckStatus.AccountRejected, result.Status);
            Assert.Equal(2, result.InvalidLine);
        }
    }

    [Fact]
    public void RejectsIncompleteOrMalformedDraft()
    {
        Assert.Equal(GlJournalDraftRecheckStatus.InvalidInput,
            GlJournalDraftRecheckRules.Validate(3, 4, Draft(), 4, [Account(7, "1002", "Cash")]).Status);
        Assert.Equal(GlJournalDraftRecheckStatus.InvalidInput,
            GlJournalDraftRecheckRules.Validate(3, 4, Draft() with { Status = GlJournalLineDraftStatus.InvalidBalance }, 4,
                [Account(7, "1002", "Cash"), Account(8, "2001", "Payable")]).Status);
        var malformed = Draft() with { Lines = [new(2, 7, "1002", "Cash", 2, 4, 8, 0), Draft().Lines[1]] };
        Assert.Equal(GlJournalDraftRecheckStatus.InvalidInput,
            GlJournalDraftRecheckRules.Validate(3, 4, malformed, 4,
                [Account(7, "1002", "Cash"), Account(8, "2001", "Payable")]).Status);
    }

    [Fact]
    public void RejectsRepeatedAccountWhenLaterFreshReadHasDifferentVersion()
    {
        var draft = Draft() with
        {
            Lines = [new(1, 7, "1002", "Cash", 2, 4, 8, 0),
                     new(2, 7, "1002", "Cash", 2, 4, 0, 8)]
        };
        var result = GlJournalDraftRecheckRules.Validate(3, 4, draft, 4,
            [Account(7, "1002", "Cash"), Account(7, "1002", "Cash", version: 3)]);
        Assert.Equal(GlJournalDraftRecheckStatus.SnapshotChanged, result.Status);
        Assert.Equal(2, result.InvalidLine);
    }

    [Fact]
    public void AllowsRepeatedIdenticalAccountSnapshotOnSeparateLegs()
    {
        var draft = Draft() with
        {
            Lines = [Draft().Lines[0], Draft().Lines[0] with { Sequence = 2, Debit = 0, Credit = 8 }]
        };
        var result = GlJournalDraftRecheckRules.Validate(3, 4, draft, 4,
            [Account(7, "1002", "Cash"), Account(7, "1002", "Cash")]);
        Assert.Equal(GlJournalDraftRecheckStatus.Allowed, result.Status);
    }

    [Theory]
    [InlineData(7, "1003", "Cash", 2)]
    [InlineData(7, "1002", "Renamed", 2)]
    [InlineData(7, "1002", "Cash", 3)]
    [InlineData(8, "1002", "Cash", 2)]
    public void RejectsContradictoryFreshSnapshotsAcrossLines(long secondId, string secondCode,
        string secondName, int secondVersion)
    {
        var draft = Draft() with
        {
            Lines = [Draft().Lines[0],
                new GlJournalLineDraft(2, secondId, secondCode, secondName, secondVersion, 4, 0, 8)]
        };
        var result = GlJournalDraftRecheckRules.Validate(3, 4, draft, 4,
            [Account(7, "1002", "Cash"), Account(secondId, secondCode, secondName, secondVersion)]);
        Assert.Equal(GlJournalDraftRecheckStatus.SnapshotConflict, result.Status);
        Assert.Equal(2, result.InvalidLine);
    }

    [Theory]
    [InlineData("", GlJournalDraftRecheckStatus.InvalidInput)]
    [InlineData("A002", GlJournalDraftRecheckStatus.AccountRejected)]
    public void RejectsMalformedLaterAccountCodeWithoutAcceptingFirstLine(string code,
        GlJournalDraftRecheckStatus expected)
    {
        var draft = Draft() with { Lines = [Draft().Lines[0], Draft().Lines[1] with { AccountCode = code }] };
        var result = GlJournalDraftRecheckRules.Validate(3, 4, draft, 4,
            [Account(7, "1002", "Cash"), Account(8, code, "Payable")]);
        Assert.Equal(expected, result.Status);
        Assert.Equal(2, result.InvalidLine);
    }

    [Theory]
    [InlineData(8, 8, GlJournalBalanceStatus.InvalidSide, 1)]
    [InlineData(8.00001, 0, GlJournalBalanceStatus.InvalidPrecision, 1)]
    [InlineData(7, 0, GlJournalBalanceStatus.Unbalanced, null)]
    public void RejectsChangedLineAmountsBeforeSaving(decimal debit, decimal credit,
        GlJournalBalanceStatus reason, int? lineNumber)
    {
        var draft = Draft() with
        {
            Lines = [Draft().Lines[0] with { Debit = debit, Credit = credit }, Draft().Lines[1]]
        };
        var result = GlJournalDraftRecheckRules.Validate(3, 4, draft, 4,
            [Account(7, "1002", "Cash"), Account(8, "2001", "Payable")]);
        Assert.Equal(GlJournalDraftRecheckStatus.InvalidBalance, result.Status);
        Assert.Equal(reason, result.BalanceStatus);
        Assert.Equal(lineNumber, result.InvalidLine);
    }

    [Theory]
    [InlineData(0, 8)]
    [InlineData(8, 9)]
    [InlineData(8.00001, 8.00001)]
    public void RejectsChangedHeaderTotals(decimal totalDebit, decimal totalCredit)
    {
        var result = GlJournalDraftRecheckRules.Validate(3, 4,
            Draft() with { TotalDebit = totalDebit, TotalCredit = totalCredit }, 4,
            [Account(7, "1002", "Cash"), Account(8, "2001", "Payable")]);
        Assert.Equal(GlJournalDraftRecheckStatus.InvalidBalance, result.Status);
        Assert.Null(result.InvalidLine);
        Assert.Null(result.BalanceStatus);
    }
}
