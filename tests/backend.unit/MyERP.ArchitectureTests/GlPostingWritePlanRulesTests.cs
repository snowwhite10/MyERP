using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Domain;

namespace MyERP.ArchitectureTests;

public sealed class GlPostingWritePlanRulesTests
{
    private static readonly GlPostingSource Source = new(3, "SD", "Invoice", 12, 2, "sha256:abc");
    private static readonly GlPostingDecision Ready = new(GlPostingDecisionStatus.Ready, PeriodId: 7);
    private static readonly GlJournalLineDraftResult Draft = new(GlJournalLineDraftStatus.Allowed,
        [new(1, 7, "1002", "現金", 2, 4, 8.5m, 0), new(2, 8, "2001", "Payable", 3, 4, 0, 8.5m)], 8.5m, 8.5m);

    private static GlPostingWritePlan Build(GlPostingDecision? decision = null, GlPostingSource? source = null,
        GlJournalLineDraftResult? draft = null, long companyId = 3, string? baseCurrency = "TWD") =>
        GlPostingWritePlanRules.Build(companyId, decision ?? Ready, source ?? Source, new(2024, 2, 29),
            baseCurrency, draft ?? Draft);

    private static void AssertEmpty(GlPostingWritePlan plan, GlPostingWritePlanStatus status)
    {
        Assert.Equal(status, plan.Status);
        Assert.Null(plan.SourceKey);
        Assert.Null(plan.Journal);
        Assert.Empty(plan.Lines);
    }

    [Fact]
    public void ReadyDecisionBuildsSourceKeyJournalAndOrderedSnapshotLines()
    {
        var plan = Build();

        Assert.Equal(GlPostingWritePlanStatus.Ready, plan.Status);
        var key = Assert.IsType<GlSourceKey>(plan.SourceKey);
        Assert.Equal((0L, 3L, "SD", "Invoice", 12L, 2, "sha256:abc"),
            (key.Id, key.CompanyId, key.SourceModule, key.SourceType, key.SourceId, key.SourceVersion, key.Fingerprint));
        var journal = Assert.IsType<GlJournal>(plan.Journal);
        Assert.Equal((0L, 3L, 7L, 0L, new DateOnly(2024, 2, 29), "TWD", GlJournalStatus.Posted, 8.5m, 8.5m),
            (journal.Id, journal.CompanyId, journal.PeriodId, journal.SourceKeyId, journal.BusinessDate,
                journal.BaseCurrency, journal.Status, journal.TotalDebit, journal.TotalCredit));
        Assert.Collection(plan.Lines,
            line => Assert.Equal((0L, 3L, 0L, 1, 7L, "1002", "現金", 2, 4, 8.5m, 0m),
                (line.Id, line.CompanyId, line.JournalId, line.Sequence, line.AccountId, line.AccountCode,
                    line.AccountName, line.AccountVersion, line.MappingVersion, line.Debit, line.Credit)),
            line => Assert.Equal((0L, 3L, 0L, 2, 8L, "2001", "Payable", 3, 4, 0m, 8.5m),
                (line.Id, line.CompanyId, line.JournalId, line.Sequence, line.AccountId, line.AccountCode,
                    line.AccountName, line.AccountVersion, line.MappingVersion, line.Debit, line.Credit)));
    }

    [Fact]
    public void PlanDoesNotStampAuditOrVersionFields()
    {
        var plan = Build();
        var rows = new MyERP.Shared.Domain.BusinessEntity[] { plan.SourceKey!, plan.Journal! }.Concat(plan.Lines);
        Assert.All(rows, row =>
        {
            Assert.Equal(default, row.CreatedAt);
            Assert.Equal(0, row.CreatedBy);
            Assert.Null(row.UpdatedAt);
            Assert.Null(row.UpdatedBy);
            Assert.Equal(0, row.Version);
        });
    }

    [Fact]
    public void NonReadyDecisionsNeverProduceRows()
    {
        foreach (var decision in new GlPostingDecision?[]
        {
            null,
            new(GlPostingDecisionStatus.Unchanged, JournalId: 44),
            new(GlPostingDecisionStatus.ReplayRejected, ReplayStatus: GlSourceReplayStatus.Conflict),
            new(GlPostingDecisionStatus.PostingRejected),
            new(GlPostingDecisionStatus.Ready),
            new(GlPostingDecisionStatus.Ready, PeriodId: 0),
            new(GlPostingDecisionStatus.Ready, PeriodId: 7, JournalId: 44),
        })
        {
            var plan = GlPostingWritePlanRules.Build(3, decision, Source, new(2024, 2, 29), "TWD", Draft);
            AssertEmpty(plan, GlPostingWritePlanStatus.NotReady);
        }
    }

    [Fact]
    public void ForeignOrInvalidHeaderInputsRejectWithoutRows()
    {
        AssertEmpty(Build(companyId: 0), GlPostingWritePlanStatus.InvalidInput);
        AssertEmpty(Build(companyId: 4), GlPostingWritePlanStatus.InvalidInput);
        AssertEmpty(GlPostingWritePlanRules.Build(3, Ready, null, new(2024, 2, 29), "TWD", Draft),
            GlPostingWritePlanStatus.InvalidInput);
        AssertEmpty(Build(source: Source with { SourceVersion = (long)int.MaxValue + 1 }),
            GlPostingWritePlanStatus.InvalidInput);
        AssertEmpty(Build(source: Source with { SourceVersion = 0 }), GlPostingWritePlanStatus.InvalidInput);
        AssertEmpty(Build(source: Source with { Fingerprint = " sha256:abc" }), GlPostingWritePlanStatus.InvalidInput);
        foreach (var currency in new string?[] { null, "", "twd", "TW", "TWDX" })
            AssertEmpty(Build(baseCurrency: currency), GlPostingWritePlanStatus.InvalidInput);
    }

    [Fact]
    public void SourceVersionAtIntBoundaryIsKept()
    {
        var plan = Build(source: Source with { SourceVersion = int.MaxValue });
        Assert.Equal(GlPostingWritePlanStatus.Ready, plan.Status);
        Assert.Equal(int.MaxValue, plan.SourceKey!.SourceVersion);
    }

    [Fact]
    public void RejectedOrMalformedDraftProducesNoRows()
    {
        var lines = Draft.Lines;
        var drafts = new GlJournalLineDraftResult?[]
        {
            null,
            Draft with { Status = GlJournalLineDraftStatus.InvalidBalance },
            Draft with { Lines = [] },
            Draft with { Lines = [lines[0]], TotalCredit = 0 },
            Draft with { Lines = [lines[0], null!] },
            Draft with { Lines = [lines[1], lines[0]] },
            Draft with { Lines = [lines[0], lines[1] with { Sequence = 3 }] },
            Draft with { TotalDebit = 9m, TotalCredit = 9m },
            Draft with { TotalCredit = 8.4m },
        };
        foreach (var draft in drafts)
        {
            var plan = GlPostingWritePlanRules.Build(3, Ready, Source, new(2024, 2, 29), "TWD", draft);
            AssertEmpty(plan, GlPostingWritePlanStatus.InvalidDraft);
        }
    }

    [Fact]
    public void RepeatedAccountLegsStaySeparateRows()
    {
        var draft = new GlJournalLineDraftResult(GlJournalLineDraftStatus.Allowed,
        [
            new(1, 7, "1002", "Cash", 2, 4, 5, 0), new(2, 7, "1002", "Cash", 2, 4, 3, 0),
            new(3, 8, "2001", "Payable", 3, 4, 0, 8),
        ], 8, 8);
        var plan = Build(draft: draft);
        Assert.Equal(GlPostingWritePlanStatus.Ready, plan.Status);
        Assert.Equal([1, 2, 3], plan.Lines.Select(line => line.Sequence));
        Assert.Equal([5m, 3m, 0m], plan.Lines.Select(line => line.Debit));
    }

    [Fact]
    public void PlanRowsAreNewInstancesNotSharedWithLaterMutation()
    {
        var first = Build();
        var second = Build();
        first.Lines[0].Debit = 99;
        first.Journal!.PeriodId = 99;
        Assert.Equal(8.5m, second.Lines[0].Debit);
        Assert.Equal(7, second.Journal!.PeriodId);
    }
}
