using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Domain;
using MyERP.Modules.MasterData.Contracts;

namespace MyERP.ArchitectureTests;

public sealed class GlPostingPreflightRulesTests
{
    private static readonly GlPostingSource Source = new(3, "SD", "Invoice", 12, 1, "sha256:abc");
    private static readonly DateOnly BusinessDate = new(2024, 2, 29);
    private static readonly GlPeriodWindow Period = new(7, 3, new(2024, 2, 1), BusinessDate, true);
    private static readonly GlJournalLineDraftResult Draft = new(GlJournalLineDraftStatus.Allowed,
        [new(1, 7, "1002", "Cash", 2, 4, 8, 0), new(2, 8, "2001", "Payable", 2, 4, 0, 8)], 8, 8);
    private static AccountInfo Account(long id, string code, string name) =>
        new(id, 3, 2, code, name, null, AccountType.Asset, BalanceDirection.Debit, true, true);

    private static GlPostingPreflightResult Check(GlPostingSource? source = null,
        IReadOnlyList<GlPeriodWindow>? periods = null, GlJournalLineDraftResult? draft = null,
        int mappingVersion = 4, IReadOnlyList<AccountInfo?>? accounts = null, long companyId = 3) =>
        GlPostingPreflightRules.Validate(companyId, source ?? Source, BusinessDate, "TWD", "TWD",
            periods ?? [Period], draft ?? Draft, 4, mappingVersion,
            accounts ?? [Account(7, "1002", "Cash"), Account(8, "2001", "Payable")]);

    [Fact]
    public void AcceptsOnlyWholeHeaderAndFreshAccountSnapshot()
    {
        Assert.Equal(new GlPostingPreflightResult(GlPostingPreflightStatus.Allowed, 7), Check());
    }

    [Fact]
    public void HeaderFailureDoesNotReturnPeriodOrAllowPosting()
    {
        var result = Check(periods: []);
        Assert.Equal(GlPostingPreflightStatus.HeaderRejected, result.Status);
        Assert.Equal(GlJournalHeaderStatus.MissingPeriod, result.HeaderStatus);
        Assert.Null(result.PeriodId);
        Assert.Null(result.DraftStatus);
    }

    [Fact]
    public void FreshMappingOrAccountFailureDoesNotExposeSelectedPeriod()
    {
        var mapping = Check(mappingVersion: 5);
        Assert.Equal(GlPostingPreflightStatus.DraftRejected, mapping.Status);
        Assert.Equal(GlJournalDraftRecheckStatus.MappingChanged, mapping.DraftStatus);
        Assert.Null(mapping.PeriodId);

        var account = Check(accounts: [Account(7, "1002", "Changed"), Account(8, "2001", "Payable")]);
        Assert.Equal(GlPostingPreflightStatus.DraftRejected, account.Status);
        Assert.Equal(GlJournalDraftRecheckStatus.SnapshotChanged, account.DraftStatus);
        Assert.Equal(1, account.InvalidLine);
        Assert.Null(account.PeriodId);
    }

    [Fact]
    public void WrongCompanyAndMutatedAmountsRejectWholePosting()
    {
        Assert.Equal(GlJournalHeaderStatus.WrongCompany,
            Check(companyId: 4).HeaderStatus);
        Assert.Equal(GlJournalHeaderStatus.InvalidDraft,
            Check(draft: Draft with { Lines = [Draft.Lines[0] with { Credit = 8 }, Draft.Lines[1]] }).HeaderStatus);
    }

    [Fact]
    public void ClosedPeriodAndMissingAccountResultsRejectWithoutPeriodId()
    {
        var closed = Check(periods: [Period with { IsOpen = false }]);
        Assert.Equal(GlPostingPreflightStatus.HeaderRejected, closed.Status);
        Assert.Equal(GlJournalHeaderStatus.ClosedPeriod, closed.HeaderStatus);
        Assert.Null(closed.PeriodId);

        var missing = Check(accounts: []);
        Assert.Equal(GlPostingPreflightStatus.DraftRejected, missing.Status);
        Assert.Equal(GlJournalDraftRecheckStatus.InvalidInput, missing.DraftStatus);
        Assert.Null(missing.PeriodId);
    }

    [Fact]
    public void StaleAccountAndMalformedHeaderReportSeparateDecisions()
    {
        var stale = Check(accounts: [Account(7, "1002", "Cash") with { Version = 3 },
            Account(8, "2001", "Payable")]);
        Assert.Equal(GlPostingPreflightStatus.DraftRejected, stale.Status);
        Assert.Equal(GlJournalDraftRecheckStatus.SnapshotChanged, stale.DraftStatus);
        Assert.Equal(1, stale.InvalidLine);

        var malformed = Check(source: Source with { Fingerprint = "invalid\nkey" });
        Assert.Equal(GlPostingPreflightStatus.HeaderRejected, malformed.Status);
        Assert.Equal(GlJournalHeaderStatus.InvalidSource, malformed.HeaderStatus);
        Assert.Null(malformed.DraftStatus);
    }
}
