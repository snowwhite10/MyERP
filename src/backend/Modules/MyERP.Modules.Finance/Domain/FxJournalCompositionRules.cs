namespace MyERP.Modules.Finance.Domain;

/// <summary>Purpose of one composed FX journal line (BR-FI-116／118, ADR-087).</summary>
public enum FxJournalLineRole
{
    /// <summary>AR/AP control: valuation change or historical base discharged by a settlement.</summary>
    Control,

    /// <summary>Unapplied foreign cash clearing released by the allocation (BR-FI-115).</summary>
    Clearing,

    RealisedGain,
    RealisedLoss,
    UnrealisedGain,
    UnrealisedLoss,

    /// <summary>Control side of the distinct reversal of the prior overlay released by a settlement.</summary>
    OverlayReversalControl,

    /// <summary>Reversal of a prior unrealised gain (debits the unrealised gain account).</summary>
    OverlayReversalGain,

    /// <summary>Reversal of a prior unrealised loss (credits the unrealised loss account).</summary>
    OverlayReversalLoss
}

/// <summary>One base-currency journal line; exactly one of <see cref="Debit"/> and <see cref="Credit"/> is positive.</summary>
public sealed record FxJournalLine(FxJournalLineRole Role, long AccountId, decimal Debit, decimal Credit);

/// <summary>Policy-resolved gain and loss accounts; they may be the same account.</summary>
public sealed record FxDifferenceAccounts(long GainAccountId, long LossAccountId);

/// <summary>Accounts for a period-end valuation journal: exposure control account and policy unrealised accounts.</summary>
public sealed record FxValuationJournalAccounts(long ControlAccountId, FxDifferenceAccounts? Unrealised);

/// <summary>
/// Accounts for a settlement journal: exposure control, unapplied cash clearing, policy realised accounts and the
/// unrealised accounts the released overlay was originally booked to.
/// </summary>
public sealed record FxSettlementJournalAccounts(
    long ControlAccountId,
    long ClearingAccountId,
    FxDifferenceAccounts? Realised,
    FxDifferenceAccounts? Unrealised);

/// <summary>
/// Composed lines; empty when nothing is posted (zero valuation delta). Totals are equal whenever valid.
/// Error collections are <c>request</c>, <c>accounts</c>, <c>valuation</c>／<c>release</c>, then <c>lines</c>.
/// </summary>
public sealed record FxJournalComposition(
    IReadOnlyList<FxJournalLine>? Lines,
    decimal TotalDebit,
    decimal TotalCredit,
    IReadOnlyList<FxAllocationFieldError> Errors)
{
    public bool IsValid => Lines is not null;
    public bool HasEntry => Lines is { Count: > 0 };
}

/// <summary>
/// Pure FX journal line composition (BR-FI-116／118, ADR-087). Turns an <see cref="FxValuationResult"/> or an
/// <see cref="FxOverlayReleaseResult"/> into balanced base-currency lines with policy-resolved accounts: the
/// difference line is always derived from the same signed amounts as the control line, so the journal balances by
/// construction and never needs a GL plug. AR control is an asset (carrying increase is a debit), AP control a
/// liability (increase is a credit). Zero lines are omitted; the final <see cref="GlJournalBalanceRules"/> preflight
/// rejects totals beyond <c>decimal(19,4)</c>. Posting, keys, locks and SQL stay with WU-FI-014a／014b.
/// </summary>
public static class FxJournalCompositionRules
{
    private const int AmountDecimals = 4;
    private const decimal SqlAmountMaximum = 999999999999999.9999m;

    public static FxJournalComposition ComposeValuation(
        FxExposureSide side,
        FxValuationJournalAccounts? accounts,
        FxValuationResult? valuation)
    {
        var errors = new List<FxAllocationFieldError>();
        var sideValid = ValidateSide(side, errors);

        if (accounts is null) errors.Add(new("accounts", null, "accounts", FxAllocationError.Required));
        else ValidateAccounts(accounts.ControlAccountId, null, null, accounts.Unrealised, errors);

        if (valuation is null)
        {
            errors.Add(new("valuation", null, "valuation", FxAllocationError.Required));
        }
        else
        {
            void Fail(string field, FxAllocationError error) => errors.Add(new("valuation", null, field, error));
            if (SignedAmountError(valuation.ValuationDelta) is { } de) Fail("valuationDelta", de);
            else if (sideValid && valuation.UnrealisedDifference != Signed(side, valuation.ValuationDelta))
                Fail("unrealisedDifference", FxAllocationError.Inconsistent);
        }

        if (errors.Count > 0) return new(null, 0m, 0m, errors);

        var lines = new List<FxJournalLine>(2);
        var delta = valuation!.ValuationDelta;
        if (delta != 0)
        {
            var controlDebit = IncreaseIsDebit(side) == delta > 0;
            var amount = Math.Abs(delta);
            var gain = valuation.UnrealisedDifference > 0;
            var u = accounts!.Unrealised!;
            Add(lines, FxJournalLineRole.Control, accounts.ControlAccountId, amount, controlDebit);
            Add(lines,
                gain ? FxJournalLineRole.UnrealisedGain : FxJournalLineRole.UnrealisedLoss,
                gain ? u.GainAccountId : u.LossAccountId,
                amount, !controlDebit);
        }

        return Balance(lines, errors);
    }

    public static FxJournalComposition ComposeSettlement(
        FxExposureSide side,
        FxSettlementJournalAccounts? accounts,
        FxOverlayReleaseResult? release)
    {
        var errors = new List<FxAllocationFieldError>();
        var sideValid = ValidateSide(side, errors);

        if (accounts is null) errors.Add(new("accounts", null, "accounts", FxAllocationError.Required));
        else ValidateAccounts(accounts.ControlAccountId, accounts.ClearingAccountId, accounts.Realised,
            accounts.Unrealised, errors, requireRealised: true);

        if (release is null)
        {
            errors.Add(new("release", null, "release", FxAllocationError.Required));
        }
        else if (release.Realised is null)
        {
            errors.Add(new("release", null, "realised", FxAllocationError.Required));
        }
        else
        {
            void Fail(string field, FxAllocationError error) => errors.Add(new("release", null, field, error));
            var r = release.Realised;
            var historicalError = AmountError(r.HistoricalBaseDischarged, allowZero: true);
            if (historicalError is { } he) Fail("historicalBaseDischarged", he);
            var settlementError = AmountError(r.SettlementBase, allowZero: false);
            if (settlementError is { } se) Fail("settlementBase", se);
            if (historicalError is null && settlementError is null && sideValid)
            {
                // Receivable: received − historical; payable: historical − paid (BR-FI-116).
                var expected = Signed(side, r.SettlementBase - r.HistoricalBaseDischarged);
                if (r.RealisedDifference != expected) Fail("realisedDifference", FxAllocationError.Inconsistent);
            }

            if (SignedAmountError(release.OverlayReleased) is { } oe) Fail("overlayReleased", oe);
        }

        if (errors.Count > 0) return new(null, 0m, 0m, errors);

        var a = accounts!;
        var rr = release!.Realised!;
        var increaseDebit = IncreaseIsDebit(side);
        var lines = new List<FxJournalLine>(5);

        // Cash clearing is consumed (AR: unapplied receipt liability debited; AP: unapplied payment asset credited)
        // and the control is discharged at historical base, on the side that decreases the carrying amount.
        Add(lines, FxJournalLineRole.Clearing, a.ClearingAccountId, rr.SettlementBase, increaseDebit);
        Add(lines, FxJournalLineRole.Control, a.ControlAccountId, rr.HistoricalBaseDischarged, !increaseDebit);
        var realisedGain = rr.RealisedDifference > 0;
        Add(lines,
            realisedGain ? FxJournalLineRole.RealisedGain : FxJournalLineRole.RealisedLoss,
            realisedGain ? a.Realised!.GainAccountId : a.Realised!.LossAccountId,
            Math.Abs(rr.RealisedDifference), !realisedGain);

        // Released overlay leaves the control: reverse the carrying change; the P&L side reverses the original
        // unrealised booking (an earlier gain is debited back, an earlier loss credited back) (BR-FI-118).
        var overlay = release.OverlayReleased;
        if (overlay != 0)
        {
            var controlDebit = increaseDebit != overlay > 0;
            var wasGain = Signed(side, overlay) > 0;
            Add(lines, FxJournalLineRole.OverlayReversalControl, a.ControlAccountId, Math.Abs(overlay), controlDebit);
            Add(lines,
                wasGain ? FxJournalLineRole.OverlayReversalGain : FxJournalLineRole.OverlayReversalLoss,
                wasGain ? a.Unrealised!.GainAccountId : a.Unrealised!.LossAccountId,
                Math.Abs(overlay), !controlDebit);
        }

        return Balance(lines, errors);
    }

    private static bool ValidateSide(FxExposureSide side, List<FxAllocationFieldError> errors)
    {
        if (Enum.IsDefined(side)) return true;
        errors.Add(new("request", null, "side", FxAllocationError.OutOfRange));
        return false;
    }

    /// <summary>
    /// Every account is positive; the control and clearing accounts are distinct from each other and from every
    /// difference account, so no line can net against the control and act as a plug. Gain and loss may share one
    /// account, and realised and unrealised accounts may be the same.
    /// </summary>
    private static void ValidateAccounts(
        long control,
        long? clearing,
        FxDifferenceAccounts? realised,
        FxDifferenceAccounts? unrealised,
        List<FxAllocationFieldError> errors,
        bool requireRealised = false)
    {
        var balanceSheet = new HashSet<long>();

        void Check(string field, long id, bool isBalanceSheet)
        {
            if (id <= 0) errors.Add(new("accounts", null, field, FxAllocationError.Required));
            else if (isBalanceSheet ? !balanceSheet.Add(id) : balanceSheet.Contains(id))
                errors.Add(new("accounts", null, field, FxAllocationError.Duplicate));
        }

        void CheckPair(string prefix, FxDifferenceAccounts? pair)
        {
            if (pair is null)
            {
                errors.Add(new("accounts", null, prefix, FxAllocationError.Required));
                return;
            }

            Check(prefix + "GainAccountId", pair.GainAccountId, false);
            Check(prefix + "LossAccountId", pair.LossAccountId, false);
        }

        Check("controlAccountId", control, true);
        if (clearing is { } c) Check("clearingAccountId", c, true);
        if (requireRealised) CheckPair("realised", realised);
        CheckPair("unrealised", unrealised);
    }

    private static FxJournalComposition Balance(List<FxJournalLine> lines, List<FxAllocationFieldError> errors)
    {
        if (lines.Count == 0) return new(lines, 0m, 0m, errors);

        var check = GlJournalBalanceRules.Validate(
            lines.Select(x => new GlJournalBalanceLeg(x.AccountId, x.Debit, x.Credit)).ToList());
        if (check.Status == GlJournalBalanceStatus.Overflow)
        {
            errors.Add(new("lines", null, "lines", FxAllocationError.Overflow));
            return new(null, 0m, 0m, errors);
        }

        // Inputs are validated and lines derived from the same amounts; anything else is a defect, not user input.
        if (check.Status != GlJournalBalanceStatus.Allowed)
            throw new InvalidOperationException($"FX journal composition produced {check.Status}.");

        return new(lines, check.TotalDebit, check.TotalCredit, errors);
    }

    private static void Add(List<FxJournalLine> lines, FxJournalLineRole role, long account, decimal amount, bool debit)
    {
        if (amount == 0) return;
        lines.Add(new(role, account, debit ? amount : 0m, debit ? 0m : amount));
    }

    /// <summary>AR control is an asset (carrying increase debits it); AP control is a liability (increase credits it).</summary>
    private static bool IncreaseIsDebit(FxExposureSide side) => side == FxExposureSide.Receivable;

    /// <summary>Signed gain for a carrying-base change: receivable keeps the sign, payable negates it.</summary>
    private static decimal Signed(FxExposureSide side, decimal amount) =>
        side == FxExposureSide.Receivable ? amount : -amount;

    private static FxAllocationError? SignedAmountError(decimal value) =>
        Math.Abs(value) > SqlAmountMaximum ? FxAllocationError.OutOfRange
        : decimal.Round(value, AmountDecimals) != value ? FxAllocationError.InvalidScale
        : null;

    private static FxAllocationError? AmountError(decimal value, bool allowZero) =>
        value < 0 || !allowZero && value == 0 ? FxAllocationError.OutOfRange
        : SignedAmountError(value);
}
