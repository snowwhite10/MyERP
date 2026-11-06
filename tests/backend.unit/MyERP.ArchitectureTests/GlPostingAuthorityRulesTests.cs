using System.Data;
using System.Net.Sockets;
using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Domain;
using MyERP.Modules.Finance.Infrastructure;
using MyERP.Modules.MasterData.Contracts;

namespace MyERP.ArchitectureTests;

/// <summary>
/// WU-FI-004a3x1b1: offline assembly of the in-transaction posting authority from raw committed rows
/// (FI-GL-AUTH-001–008) and the connection-lost classification of the SQL port (FI-GL-CONN-001–003).
/// </summary>
public sealed class GlPostingAuthorityRulesTests
{
    private static readonly GlPostingSource Source = new(3, "SD", "Invoice", 12, 1, "sha256:abc");
    private static readonly GlJournalLineDraftResult Draft = new(GlJournalLineDraftStatus.Allowed,
        [new(1, 8, "2001", "Payable", 2, 4, 0, 5), new(2, 7, "1002", "Cash", 2, 4, 8, 0),
         new(3, 8, "2001", "Payable", 2, 4, 0, 3)], 8, 8);
    private static readonly GlPeriodWindow OpenFebruary = new(7, 3, new(2024, 2, 1), new(2024, 2, 29), true);

    private static AccountInfo Account(long id, string code, string name, long companyId = 3) =>
        new(id, companyId, 2, code, name, null, AccountType.Asset, BalanceDirection.Debit, true, true);

    private static Dictionary<string, AccountInfo?> Accounts() => new(StringComparer.Ordinal)
    {
        ["1002"] = Account(7, "1002", "Cash"),
        ["2001"] = Account(8, "2001", "Payable"),
    };

    private static GlCommittedSourceRow Row(long companyId = 3, long? journalId = 501, string fingerprint = "sha256:abc") =>
        new(companyId, "SD", "Invoice", 12, 1, fingerprint, journalId);

    private static GlPostingAuthority? Assemble(GlCommittedSourceRow? committed = null,
        IReadOnlyList<GlPeriodWindow>? periods = null, GlJournalLineDraftResult? draft = null,
        IReadOnlyDictionary<string, AccountInfo?>? accounts = null, long companyId = 3, bool nullPeriods = false,
        bool nullAccounts = false, string? baseCurrency = "TWD", int mappingVersion = 4) =>
        GlPostingAuthorityRules.Assemble(companyId, committed, baseCurrency,
            nullPeriods ? null : periods ?? [OpenFebruary], mappingVersion, draft ?? Draft,
            nullAccounts ? null : accounts ?? Accounts());

    private static GlPostingDecision Decide(GlPostingAuthority authority, GlJournalLineDraftResult? draft = null) =>
        GlPostingDecisionRules.Decide(3, Source, authority.Committed, new(2024, 2, 29), "TWD",
            authority.CompanyBaseCurrency, authority.Periods, draft ?? Draft, 4, authority.CurrentMappingVersion,
            authority.CurrentAccounts);

    [Fact(DisplayName = "FI-GL-AUTH-001 accounts follow draft line order (repeated codes repeated); MD read list is distinct and ordinal-sorted")]
    public void Normal_AccountsInDraftOrder()
    {
        Assert.Equal(["1002", "2001"], GlPostingAuthorityRules.AccountCodesToRead(Draft));

        var authority = Assemble();

        Assert.NotNull(authority);
        Assert.Null(authority.Committed);
        Assert.Equal("TWD", authority.CompanyBaseCurrency);
        Assert.Equal(4, authority.CurrentMappingVersion);
        Assert.Equal([OpenFebruary], authority.Periods);
        Assert.Equal(["2001", "1002", "2001"], authority.CurrentAccounts!.Select(a => a!.Code));
        Assert.Equal(GlPostingDecisionStatus.Ready, Decide(authority).Status);
    }

    [Fact(DisplayName = "FI-GL-AUTH-002 committed row maps to the canonical committed source: same → Unchanged, other fingerprint → conflict")]
    public void Duplicate_CommittedRowMapsToReplay()
    {
        var same = Assemble(Row());
        Assert.Equal(new GlCommittedSource(Source, 501), same!.Committed);
        var unchanged = Decide(same);
        Assert.Equal(GlPostingDecisionStatus.Unchanged, unchanged.Status);
        Assert.Equal(501, unchanged.JournalId);

        var other = Decide(Assemble(Row(fingerprint: "sha256:def"))!);
        Assert.Equal(GlPostingDecisionStatus.ReplayRejected, other.Status);
        Assert.Equal(GlSourceReplayStatus.Conflict, other.ReplayStatus);
    }

    [Theory(DisplayName = "FI-GL-AUTH-003 a committed source key without its journal is a malformed read (null → 500)")]
    [InlineData(null)]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void Error_CommittedRowWithoutJournal(long? journalId) =>
        Assert.Null(Assemble(Row(journalId: journalId)));

    [Fact(DisplayName = "FI-GL-AUTH-004 rows of another company (source key, period, account) are a query-filter breach (null)")]
    public void Permission_ForeignCompanyRows()
    {
        Assert.Null(Assemble(Row(companyId: 4)));
        Assert.Null(Assemble(periods: [OpenFebruary, new(9, 4, new(2024, 3, 1), new(2024, 3, 31), true)]));
        var accounts = Accounts();
        accounts["1002"] = Account(7, "1002", "Cash", companyId: 4);
        Assert.Null(Assemble(accounts: accounts));
        Assert.Null(Assemble(companyId: 0));
        Assert.Null(Assemble(companyId: -3));
    }

    [Fact(DisplayName = "FI-GL-AUTH-005 an account read as missing stays a null entry at its line and posting is rejected with no plan")]
    public void Relation_MissingAccountIsNullEntry()
    {
        var accounts = Accounts();
        accounts["2001"] = null;

        var authority = Assemble(accounts: accounts);

        Assert.NotNull(authority);
        Assert.Equal([false, true, false], authority.CurrentAccounts!.Select(a => a is not null));
        Assert.Equal(GlPostingDecisionStatus.PostingRejected, Decide(authority).Status);
    }

    [Fact(DisplayName = "FI-GL-AUTH-006 unread codes, null account map, case-different or mismatched account code are malformed (null)")]
    public void Error_MalformedAccountRead()
    {
        Assert.Null(Assemble(nullAccounts: true));
        Assert.Null(Assemble(accounts: new Dictionary<string, AccountInfo?>(StringComparer.Ordinal)
        {
            ["1002"] = Account(7, "1002", "Cash"),
        }));
        var ignoreCase = new Dictionary<string, AccountInfo?>(StringComparer.OrdinalIgnoreCase)
        {
            ["CASH-A"] = Account(7, "CASH-A", "Cash"),
        };
        var lowerDraft = Draft with { Lines = [Draft.Lines[0] with { AccountCode = "cash-a" }] };
        Assert.Null(Assemble(draft: lowerDraft, accounts: ignoreCase));
        var mismatched = Accounts();
        mismatched["2001"] = Account(8, "2002", "Payable");
        Assert.Null(Assemble(accounts: mismatched));
        Assert.Null(Assemble(nullPeriods: true));
    }

    [Fact(DisplayName = "FI-GL-AUTH-007 missing or empty draft reads no account and yields an empty list (decision rejects the draft)")]
    public void Boundary_NoDraftLines()
    {
        var empty = new GlJournalLineDraftResult(GlJournalLineDraftStatus.InvalidInput, []);
        Assert.Empty(GlPostingAuthorityRules.AccountCodesToRead(null));
        Assert.Empty(GlPostingAuthorityRules.AccountCodesToRead(empty));

        var authority = GlPostingAuthorityRules.Assemble(3, null, "TWD", [OpenFebruary], 4, null, null);
        Assert.NotNull(authority);
        Assert.Empty(authority.CurrentAccounts!);
        Assert.Empty(Assemble(draft: empty, nullAccounts: true)!.CurrentAccounts!);
        Assert.NotEqual(GlPostingDecisionStatus.Ready, Decide(authority, empty).Status);
    }

    [Fact(DisplayName = "FI-GL-AUTH-008 closed period, other base currency or newer mapping version pass through and the decision rejects them")]
    public void State_AuthorityValuesPassThrough()
    {
        var closed = Assemble(periods: [OpenFebruary with { IsOpen = false }]);
        Assert.False(closed!.Periods![0].IsOpen);
        Assert.Equal(GlPostingDecisionStatus.PostingRejected, Decide(closed).Status);

        var currency = Assemble(baseCurrency: "USD");
        Assert.Equal("USD", currency!.CompanyBaseCurrency);
        Assert.Equal(GlPostingDecisionStatus.PostingRejected, Decide(currency).Status);

        var mapping = Assemble(mappingVersion: 5);
        Assert.Equal(5, mapping!.CurrentMappingVersion);
        Assert.Equal(GlPostingDecisionStatus.PostingRejected, Decide(mapping).Status);

        Assert.Empty(Assemble(periods: [])!.Periods!);
    }

    [Fact(DisplayName = "FI-GL-CONN-001 transport exceptions anywhere in the chain mean the connection is lost")]
    public void Connection_TransportExceptions()
    {
        Assert.True(GlPostingConnectionRules.IsConnectionLost(new IOException("reset"), ConnectionState.Open));
        Assert.True(GlPostingConnectionRules.IsConnectionLost(
            new SocketException((int)SocketError.ConnectionReset), ConnectionState.Open));
        Assert.True(GlPostingConnectionRules.IsConnectionLost(
            new InvalidOperationException("outer", new InvalidDataException("mid", new IOException("reset"))), ConnectionState.Open));
    }

    [Theory(DisplayName = "FI-GL-CONN-002 a broken or closed connection after the failure is lost, whatever the exception")]
    [InlineData(ConnectionState.Broken)]
    [InlineData(ConnectionState.Closed)]
    public void Connection_BrokenState(ConnectionState state)
    {
        Assert.True(GlPostingConnectionRules.IsConnectionLost(new InvalidOperationException("x"), state));
        Assert.True(GlPostingConnectionRules.IsConnectionLost(null, state));
    }

    [Fact(DisplayName = "FI-GL-CONN-003 ordinary failures on an open connection are not connection loss")]
    public void Connection_OrdinaryFailures()
    {
        Assert.False(GlPostingConnectionRules.IsConnectionLost(new InvalidOperationException("x"), ConnectionState.Open));
        Assert.False(GlPostingConnectionRules.IsConnectionLost(new ArgumentException("x"), ConnectionState.Open));
        Assert.False(GlPostingConnectionRules.IsConnectionLost(null, ConnectionState.Open));
        Assert.False(GlPostingConnectionRules.IsConnectionLost(new TimeoutException("x"), ConnectionState.Executing));
    }
}
