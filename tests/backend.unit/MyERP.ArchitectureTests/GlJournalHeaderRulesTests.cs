using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Domain;

namespace MyERP.ArchitectureTests;

public sealed class GlJournalHeaderRulesTests
{
    private static readonly DateOnly BusinessDate = new(2024, 2, 29);
    private static readonly GlPostingSource Source = new(3, "SD", "Invoice", 12, 1, "sha256:abc");
    private static readonly GlPeriodWindow Period = new(7, 3, new(2024, 2, 1), BusinessDate, true);
    private static readonly GlJournalLineDraftResult Draft = new(GlJournalLineDraftStatus.Allowed,
        [new(1, 7, "1002", "Cash", 2, 4, 8, 0), new(2, 8, "2001", "Payable", 2, 4, 0, 8)], 8, 8);

    private static GlJournalHeaderResult Check(GlPostingSource? source = null, string? currency = "TWD",
        string? authoritativeCurrency = "TWD", IReadOnlyList<GlPeriodWindow>? periods = null,
        GlJournalLineDraftResult? draft = null, long companyId = 3) =>
        GlJournalHeaderRules.Validate(companyId, source ?? Source, BusinessDate, currency,
            authoritativeCurrency, periods ?? [Period], draft ?? Draft);

    [Fact]
    public void AcceptsSavedSourceCompanyCurrencyBalancedDraftAndInclusiveLeapDay()
    {
        Assert.Equal(new GlJournalHeaderResult(GlJournalHeaderStatus.Allowed, 7), Check());
    }

    [Fact]
    public void RejectsForeignOrInvalidSourceBeforePeriodSelection()
    {
        Assert.Equal(GlJournalHeaderStatus.WrongCompany, Check(source: Source with { CompanyId = 4 }).Status);
        Assert.Equal(GlJournalHeaderStatus.InvalidSource, Check(source: Source with { Fingerprint = "bad\nkey" }).Status);
    }

    [Theory]
    [InlineData("usd", "USD", GlJournalHeaderStatus.InvalidCurrency)]
    [InlineData("US", "USD", GlJournalHeaderStatus.InvalidCurrency)]
    [InlineData("USD", "TWD", GlJournalHeaderStatus.CurrencyMismatch)]
    [InlineData("USD", "usd", GlJournalHeaderStatus.InvalidCurrency)]
    public void RejectsInvalidOrNonBaseCurrency(string currency, string authority, GlJournalHeaderStatus status)
    {
        Assert.Equal(status, Check(currency: currency, authoritativeCurrency: authority).Status);
    }

    [Fact]
    public void RejectsMissingClosedAndAmbiguousPeriodsWithoutReturningAnId()
    {
        Assert.Equal(new GlJournalHeaderResult(GlJournalHeaderStatus.MissingPeriod), Check(periods: []));
        Assert.Equal(new GlJournalHeaderResult(GlJournalHeaderStatus.ClosedPeriod),
            Check(periods: [Period with { IsOpen = false }]));
        Assert.Equal(new GlJournalHeaderResult(GlJournalHeaderStatus.AmbiguousPeriod),
            Check(periods: [Period, Period with { Id = 8 }]));
        Assert.Equal(new GlJournalHeaderResult(GlJournalHeaderStatus.MissingPeriod),
            Check(periods: [Period with { CompanyId = 4 }]));
    }

    [Fact]
    public void RejectsIncompleteOrUnbalancedDraftBeforeSelectingPeriod()
    {
        Assert.Equal(GlJournalHeaderStatus.InvalidDraft,
            Check(draft: Draft with { Status = GlJournalLineDraftStatus.InvalidBalance }).Status);
        Assert.Equal(GlJournalHeaderStatus.InvalidDraft,
            Check(draft: Draft with { TotalCredit = 7 }).Status);
        Assert.Equal(GlJournalHeaderStatus.InvalidDraft,
            Check(draft: Draft with { Lines = [null!, Draft.Lines[1]] }).Status);
    }

    [Fact]
    public void RejectsMissingInputs()
    {
        Assert.Equal(GlJournalHeaderStatus.InvalidInput, Check(companyId: 0).Status);
        Assert.Equal(GlJournalHeaderStatus.InvalidCurrency, Check(currency: null).Status);
        Assert.Equal(GlJournalHeaderStatus.InvalidInput,
            GlJournalHeaderRules.Validate(3, Source, BusinessDate, "TWD", "TWD", null, Draft).Status);
    }
}
