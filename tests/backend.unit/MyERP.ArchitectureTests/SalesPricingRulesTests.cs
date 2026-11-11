using MyERP.Modules.Sales.Application;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

/// <summary>WU-SD-005a0 offline pricing rules (SD-PRICE-U01～U14).</summary>
public sealed class SalesPricingRulesTests
{
    private static readonly DateOnly Date = new(2028, 2, 29);

    private static SalesPriceRuleSnapshot Rule(long id, long? customerId = null,
        DateOnly? from = null, DateOnly? to = null, decimal price = 100m, decimal ceiling = 10m,
        bool active = true, bool deleted = false, long companyId = 1, long itemId = 7,
        string currency = "TWD", int version = 1) =>
        new(id, version, companyId, itemId, customerId, currency,
            from ?? new DateOnly(2028, 1, 1), to ?? new DateOnly(2028, 12, 31),
            price, ceiling, active, deleted);

    private static SalesPriceLookup Lookup(DateOnly? date = null, long customerId = 50,
        string currency = "TWD") => new(1, 7, customerId, currency, date ?? Date);

    // SD-PRICE-U15～U18: the dated advisory quote exposes only the deciding rule.
    [Fact]
    public void QuoteCarriesDateAndSelectedRuleWithoutMutatingCandidates()
    {
        var candidates = new[] { Rule(1), Rule(2, customerId: 50, price: 90m, ceiling: 5m, version: 3) };
        var before = candidates.ToArray();
        var quote = SalesPricingRules.Quote(Lookup(), candidates);
        quote.AsOf.Should().Be(Date);
        quote.Status.Should().Be(SalesPriceSelectionStatus.Selected);
        quote.RuleId.Should().Be(2);
        quote.RuleVersion.Should().Be(3);
        quote.UnitPrice.Should().Be(90m);
        quote.DiscountCeiling.Should().Be(5m);
        quote.ExpiresOn.Should().Be(new DateOnly(2028, 12, 31));
        quote.ErrorCode.Should().BeNull();
        candidates.Should().Equal(before);
    }

    [Fact]
    public void QuoteNeverExposesAnExpiredOrDeletedPrice()
    {
        var rule = Rule(2, customerId: 50, to: new DateOnly(2028, 2, 29));
        SalesPricingRules.Quote(Lookup(), [rule]).RuleId.Should().Be(2);
        foreach (var candidate in new[] { rule, rule with { IsActive = false }, rule with { IsDeleted = true } })
        {
            var date = candidate == rule ? new DateOnly(2028, 3, 1) : Date;
            var quote = SalesPricingRules.Quote(Lookup(date), [candidate]);
            quote.Status.Should().Be(SalesPriceSelectionStatus.NotFound);
            quote.ErrorCode.Should().Be(SalesPricingErrors.RuleNotFound);
            quote.RuleId.Should().BeNull();
            quote.UnitPrice.Should().BeNull();
        }
    }

    [Fact]
    public void QuoteReportsConflictsWithoutGuessingAPrice()
    {
        var quote = SalesPricingRules.Quote(Lookup(),
            [Rule(8, customerId: 50), Rule(3, customerId: 50)]);
        quote.Status.Should().Be(SalesPriceSelectionStatus.DuplicateActiveRule);
        quote.ErrorCode.Should().Be(SalesPricingErrors.DuplicateActiveRule);
        quote.ConflictingRuleIds.Should().Equal(3, 8);
        quote.RuleId.Should().BeNull();
        quote.UnitPrice.Should().BeNull();
    }

    [Fact]
    public void QuoteValidatesLookupAndNeverSelectsAnotherCompanyOrCurrency()
    {
        FluentActions.Invoking(() => SalesPricingRules.Quote(Lookup(currency: " "), []))
            .Should().Throw<RequestValidationException>();
        var quote = SalesPricingRules.Quote(Lookup(),
            [Rule(1, companyId: 2), Rule(2, currency: "USD"), Rule(3, customerId: 51)]);
        quote.Status.Should().Be(SalesPriceSelectionStatus.NotFound);
    }

    // SD-PRICE-U01 (SD-PRICE-002)
    [Fact]
    public void CustomerSpecificRuleWinsOverDefault()
    {
        var result = SalesPricingRules.Select(Lookup(), [Rule(1), Rule(2, customerId: 50, price: 90m)]);
        result.Status.Should().Be(SalesPriceSelectionStatus.Selected);
        result.Rule!.RuleId.Should().Be(2);
        result.ErrorCode.Should().BeNull();
    }

    // SD-PRICE-U02 (SD-PRICE-002)
    [Fact]
    public void FallsBackToDefaultWhenOtherCustomerCurrencyCompanyOrItemOnly()
    {
        var result = SalesPricingRules.Select(Lookup(),
        [
            Rule(1), Rule(2, customerId: 51), Rule(3, customerId: 50, currency: "USD"),
            Rule(4, customerId: 50, companyId: 2), Rule(5, customerId: 50, itemId: 8),
        ]);
        result.Status.Should().Be(SalesPriceSelectionStatus.Selected);
        result.Rule!.RuleId.Should().Be(1);
    }

    // SD-PRICE-U03 (SD-PRICE-005): inclusive validity across leap day / year end.
    [Theory]
    [InlineData(2028, 2, 29, true)]
    [InlineData(2028, 3, 1, true)]
    [InlineData(2028, 2, 28, false)]
    [InlineData(2028, 3, 2, false)]
    public void ValidityBoundsAreInclusive(int y, int m, int d, bool found)
    {
        var rule = Rule(1, from: new DateOnly(2028, 2, 29), to: new DateOnly(2028, 3, 1));
        var result = SalesPricingRules.Select(Lookup(new DateOnly(y, m, d)), [rule]);
        result.Status.Should().Be(found ? SalesPriceSelectionStatus.Selected : SalesPriceSelectionStatus.NotFound);
        if (!found) result.ErrorCode.Should().Be(SalesPricingErrors.RuleNotFound);
    }

    // SD-PRICE-U04 (SD-PRICE-005/014): inactive, deleted, expired and future rules are never guessed.
    [Fact]
    public void InactiveDeletedOrOutOfDateRulesYieldNotFound()
    {
        var result = SalesPricingRules.Select(Lookup(),
        [
            Rule(1, customerId: 50, active: false), Rule(2, deleted: true),
            Rule(3, to: new DateOnly(2028, 2, 28)), Rule(4, from: new DateOnly(2028, 3, 1)),
        ]);
        result.Status.Should().Be(SalesPriceSelectionStatus.NotFound);
        result.Rule.Should().BeNull();
        result.ErrorCode.Should().Be(SalesPricingErrors.RuleNotFound);
    }

    // SD-PRICE-U05 (BR-SD-027): two candidates on the winning tier are an integrity conflict.
    [Fact]
    public void DuplicateCandidatesOnDecidingTierAreConflictNotGuess()
    {
        var result = SalesPricingRules.Select(Lookup(),
            [Rule(9, customerId: 50), Rule(3, customerId: 50, price: 1m), Rule(1)]);
        result.Status.Should().Be(SalesPriceSelectionStatus.DuplicateActiveRule);
        result.Rule.Should().BeNull();
        result.ErrorCode.Should().Be(SalesPricingErrors.DuplicateActiveRule);
        result.ConflictingRuleIds.Should().Equal(3, 9);
    }

    // SD-PRICE-U06: a single customer rule still wins although default tier is duplicated.
    [Fact]
    public void DuplicateDefaultTierDoesNotBlockSingleCustomerRule()
    {
        var result = SalesPricingRules.Select(Lookup(), [Rule(1), Rule(2), Rule(3, customerId: 50)]);
        result.Rule!.RuleId.Should().Be(3);
        var fallback = SalesPricingRules.Select(Lookup(customerId: 60), [Rule(1), Rule(2), Rule(3, customerId: 50)]);
        fallback.Status.Should().Be(SalesPriceSelectionStatus.DuplicateActiveRule);
        fallback.ConflictingRuleIds.Should().Equal(1, 2);
    }

    // SD-PRICE-U07 (SD-PRICE-006): malformed lookup or snapshot is 400 before any selection.
    [Fact]
    public void RejectsInvalidLookupAndSnapshots()
    {
        Action[] invalid =
        [
            () => SalesPricingRules.Select(new SalesPriceLookup(0, 7, 50, "TWD", Date), []),
            () => SalesPricingRules.Select(new SalesPriceLookup(1, 0, 50, "TWD", Date), []),
            () => SalesPricingRules.Select(new SalesPriceLookup(1, 7, 0, "TWD", Date), []),
            () => SalesPricingRules.Select(new SalesPriceLookup(1, 7, 50, " ", Date), []),
            () => SalesPricingRules.Select(Lookup(), [null!]),
            () => SalesPricingRules.Select(Lookup(), [Rule(1), Rule(1, customerId: 50)]),
        ];
        foreach (var act in invalid)
            act.Should().Throw<RequestValidationException>();
    }

    // SD-PRICE-U08 (SD-PRICE-008): equal-scope intervals sharing a boundary or enclosing overlap.
    [Fact]
    public void FindsEqualScopeOverlapsIncludingSharedBoundary()
    {
        var proposed = Rule(0, from: new DateOnly(2028, 3, 1), to: new DateOnly(2028, 6, 30));
        var overlaps = SalesPricingRules.FindOverlaps(proposed,
        [
            Rule(5, from: new DateOnly(2028, 1, 1), to: new DateOnly(2028, 3, 1)),
            Rule(2, from: new DateOnly(2028, 4, 1), to: new DateOnly(2028, 4, 30)),
            Rule(8, from: new DateOnly(2027, 1, 1), to: new DateOnly(2029, 1, 1)),
            Rule(3, from: new DateOnly(2028, 1, 1), to: new DateOnly(2028, 2, 29)),
            Rule(4, from: new DateOnly(2028, 7, 1), to: new DateOnly(2028, 12, 31)),
        ]);
        overlaps.Should().Equal(2, 5, 8);
    }

    // SD-PRICE-U09 (SD-PRICE-008/014): default and customer scopes coexist; inactive, deleted and self ignored.
    [Fact]
    public void OverlapIgnoresOtherScopesInactiveDeletedAndSelf()
    {
        var proposed = Rule(10, customerId: 50);
        SalesPricingRules.FindOverlaps(proposed,
        [
            Rule(10, customerId: 50), Rule(1), Rule(2, customerId: 51),
            Rule(3, customerId: 50, active: false), Rule(4, customerId: 50, deleted: true),
            Rule(5, customerId: 50, currency: "USD"), Rule(6, customerId: 50, itemId: 8),
            Rule(7, customerId: 50, companyId: 2),
        ]).Should().BeEmpty();
        SalesPricingRules.FindOverlaps(proposed with { IsActive = false }, [Rule(11, customerId: 50)])
            .Should().BeEmpty();
        SalesPricingRules.FindOverlaps(proposed, [Rule(11, customerId: 50)]).Should().Equal(11);
    }

    // SD-PRICE-U10 (SD-PRICE-006): rule input field validation in form order.
    [Fact]
    public void ValidatesRuleInputByField()
    {
        var valid = new SalesPriceRuleInput(7, null, "TWD", new DateOnly(2028, 1, 1),
            new DateOnly(2028, 1, 1), 0m, 100m);
        SalesPricingRules.ValidateRule(valid);
        SalesPricingRules.ValidateRule(valid with { UnitPrice = 9_999_999_999_999.999999m, DiscountCeiling = 0m });

        var bad = new SalesPriceRuleInput(0, 0, "tw", new DateOnly(2028, 2, 1),
            new DateOnly(2028, 1, 31), 1.0000001m, 100.0001m);
        var errors = FluentActions.Invoking(() => SalesPricingRules.ValidateRule(bad))
            .Should().Throw<RequestValidationException>().Which.Errors;
        errors.Keys.Should().Equal("itemId", "customerId", "currencyCode", "validTo", "unitPrice", "discountCeiling");

        var more = FluentActions.Invoking(() => SalesPricingRules.ValidateRule(
                valid with { UnitPrice = -0.000001m, DiscountCeiling = -0.0001m }))
            .Should().Throw<RequestValidationException>().Which.Errors;
        more.Keys.Should().Equal("unitPrice", "discountCeiling");
        FluentActions.Invoking(() => SalesPricingRules.ValidateRule(valid with { UnitPrice = 10_000_000_000_000m }))
            .Should().Throw<RequestValidationException>().Which.Errors.Should().ContainKey("unitPrice");
        FluentActions.Invoking(() => SalesPricingRules.ValidateRule(valid with { DiscountCeiling = 1.00001m }))
            .Should().Throw<RequestValidationException>().Which.Errors.Should().ContainKey("discountCeiling");
    }

    private static SalesPriceSelection Selected(decimal price = 100m, decimal ceiling = 10m) =>
        SalesPricingRules.Select(Lookup(), [Rule(4, price: price, ceiling: ceiling, version: 3)]);

    private static SalesPriceSelection NotFound() => SalesPricingRules.Select(Lookup(), []);

    // SD-PRICE-U21/U22: save resolves the rule from its own business context, then decides.
    [Fact]
    public void SaveDecisionUsesCurrentBusinessDateAndRejectsStaleQuote()
    {
        var rule = Rule(4, to: new DateOnly(2028, 2, 29), version: 3);
        var line = new SalesPriceLineRequest(100m, 0m, 4, 3, null);
        SalesPricingRules.DecideForSave(Lookup(), [rule], line, false, "lines[0]")
            .RuleId.Should().Be(4);
        FluentActions.Invoking(() => SalesPricingRules.DecideForSave(
                Lookup(new DateOnly(2028, 3, 1)), [rule], line, true, "lines[0]"))
            .Should().Throw<ConflictException>().Which.ErrorCode.Should().Be(SalesPricingErrors.StaleQuote);
    }

    [Fact]
    public void SaveDecisionDoesNotReuseSelectionFromOtherCustomerOrCurrency()
    {
        var rule = Rule(4, customerId: 50, version: 3);
        var line = new SalesPriceLineRequest(100m, 0m, 4, 3, null);
        foreach (var lookup in new[] { Lookup(customerId: 51), Lookup(currency: "USD") })
            FluentActions.Invoking(() => SalesPricingRules.DecideForSave(
                    lookup, [rule], line, true, "lines[0]"))
                .Should().Throw<ConflictException>().Which.ErrorCode.Should().Be(SalesPricingErrors.StaleQuote);

        var manual = SalesPricingRules.DecideForSave(Lookup(customerId: 51), [rule],
            new SalesPriceLineRequest(90m, 0m, null, null, "  approved  "), true, "lines[0]");
        manual.RuleId.Should().BeNull();
        manual.IsOverride.Should().BeTrue();
        manual.OverrideReason.Should().Be("approved");
    }

    // SD-PRICE-U19/U20: malformed quoted identity is a field error before any conflict or grant check.
    [Fact]
    public void NonpositiveQuoteHintsAreFieldErrorsBeforeConflictsAndOverride()
    {
        var duplicate = SalesPricingRules.Select(Lookup(), [Rule(1), Rule(2)]);
        foreach (var selection in new[] { Selected(), duplicate, NotFound() })
        {
            foreach (var line in new[]
            {
                new SalesPriceLineRequest(100m, 0m, 0, 3, null),
                new SalesPriceLineRequest(100m, 0m, -1, 3, null),
                new SalesPriceLineRequest(100m, 0m, 4, 0, null),
                new SalesPriceLineRequest(100m, 0m, 4, -1, null),
            })
            {
                var error = FluentActions.Invoking(() => SalesPricingRules.Decide(selection, line, false, "lines[0]"))
                    .Should().Throw<RequestValidationException>().Which;
                error.Errors.Should().ContainKey(line.QuotedRuleId <= 0
                    ? "lines[0].pricingRuleId" : "lines[0].pricingRuleVersion");
            }
            var both = FluentActions.Invoking(() => SalesPricingRules.Decide(selection,
                new SalesPriceLineRequest(100m, 0m, 0, 0, null), false, "lines[1]"))
                .Should().Throw<RequestValidationException>().Which;
            both.Errors.Keys.Should().Equal("lines[1].pricingRuleId", "lines[1].pricingRuleVersion");
        }
    }

    // SD-PRICE-U11 (SD-PRICE-007): exact ceiling is ordinary; reason ignored when not overriding.
    [Fact]
    public void RulePriceWithinCeilingIsOrdinary()
    {
        var decision = SalesPricingRules.Decide(Selected(),
            new SalesPriceLineRequest(100m, 10.0000m, 4, 3, "  ignored  "), hasOverrideGrant: false, "lines[0]");
        decision.Should().Be(new SalesPriceDecision(4, 3, 100m, 10m, 100m, 10m, false, null));
    }

    // SD-PRICE-U12 (SD-PRICE-004/007/BR-SD-028): override needed for 0.0001% over ceiling, manual price or no rule.
    [Fact]
    public void OverrideRequiresGrantThenTrimmedReason()
    {
        var cases = new (SalesPriceSelection Selection, SalesPriceLineRequest Line)[]
        {
            (Selected(), new(100m, 10.0001m, null, null, null)),
            (Selected(), new(99.999999m, 0m, null, null, null)),
            (NotFound(), new(100m, 0m, null, null, null)),
        };
        foreach (var (selection, line) in cases)
        {
            FluentActions.Invoking(() => SalesPricingRules.Decide(selection, line, false, "lines[1]"))
                .Should().Throw<ForbiddenException>().Which.ErrorCode.Should().Be(SalesPricingErrors.OverrideRequired);
            FluentActions.Invoking(() => SalesPricingRules.Decide(selection, line with { OverrideReason = "   " }, true, "lines[1]"))
                .Should().Throw<RequestValidationException>().Which.Errors.Should().ContainKey("lines[1].overrideReason");
            FluentActions.Invoking(() => SalesPricingRules.Decide(selection,
                    line with { OverrideReason = new string('x', 201) }, true, "lines[1]"))
                .Should().Throw<RequestValidationException>().Which.Errors.Should().ContainKey("lines[1].overrideReason");
            var granted = SalesPricingRules.Decide(selection, line with { OverrideReason = "  VIP  " }, true, "lines[1]");
            granted.IsOverride.Should().BeTrue();
            granted.OverrideReason.Should().Be("VIP");
            granted.UnitPrice.Should().Be(line.UnitPrice);
            granted.DiscountRate.Should().Be(line.DiscountRate);
        }
        var noRule = SalesPricingRules.Decide(NotFound(), new(5m, 0m, null, null, new string('x', 200)), true, "l");
        noRule.RuleId.Should().BeNull();
        noRule.RuleVersion.Should().BeNull();
        noRule.RulePrice.Should().BeNull();
        noRule.RuleDiscountCeiling.Should().BeNull();
    }

    // SD-PRICE-U13 (SD-PRICE-006/BR-SD-028): out-of-range values are never overridable; duplicates never guessed.
    [Fact]
    public void InvalidValuesAndDuplicateRulesAreNotOverridable()
    {
        var bad = new SalesPriceLineRequest[]
        {
            new(-0.000001m, 0m, null, null, "x"), new(1.0000001m, 0m, null, null, "x"),
            new(10_000_000_000_000m, 0m, null, null, "x"),
            new(1m, -0.0001m, null, null, "x"), new(1m, 100.0001m, null, null, "x"),
            new(1m, 1.00001m, null, null, "x"),
        };
        foreach (var line in bad)
            FluentActions.Invoking(() => SalesPricingRules.Decide(Selected(), line, true, "lines[2]"))
                .Should().Throw<RequestValidationException>();
        FluentActions.Invoking(() => SalesPricingRules.Decide(Selected(), new(-1m, 101m, null, null, "x"), true, "lines[2]"))
            .Should().Throw<RequestValidationException>().Which.Errors.Keys
            .Should().Equal("lines[2].unitPrice", "lines[2].discountRate");

        var duplicate = SalesPricingRules.Select(Lookup(), [Rule(1), Rule(2)]);
        FluentActions.Invoking(() => SalesPricingRules.Decide(duplicate, new(100m, 0m, null, null, "x"), true, "l"))
            .Should().Throw<ConflictException>().Which.ErrorCode.Should().Be(SalesPricingErrors.DuplicateActiveRule);
    }

    // SD-PRICE-U14 (SD-PRICE-010): a stale or half quote hint is 409/400 before override checks.
    [Fact]
    public void StaleQuoteHintIsConflictAndHalfHintIsBadRequest()
    {
        var stale = new SalesPriceLineRequest[]
        {
            new(100m, 0m, 4, 2, null), new(100m, 0m, 5, 3, null),
        };
        foreach (var line in stale)
            FluentActions.Invoking(() => SalesPricingRules.Decide(Selected(), line, false, "l"))
                .Should().Throw<ConflictException>().Which.ErrorCode.Should().Be(SalesPricingErrors.StaleQuote);
        FluentActions.Invoking(() => SalesPricingRules.Decide(NotFound(), new(100m, 0m, 4, 3, null), true, "l"))
            .Should().Throw<ConflictException>().Which.ErrorCode.Should().Be(SalesPricingErrors.StaleQuote);
        FluentActions.Invoking(() => SalesPricingRules.Decide(Selected(), new(100m, 0m, 4, null, null), false, "lines[0]"))
            .Should().Throw<RequestValidationException>().Which.Errors.Should().ContainKey("lines[0].pricingRuleVersion");
        FluentActions.Invoking(() => SalesPricingRules.Decide(Selected(), new(100m, 0m, null, 3, null), false, "lines[0]"))
            .Should().Throw<RequestValidationException>().Which.Errors.Should().ContainKey("lines[0].pricingRuleId");
    }

    // SD-PRICE-U23: client field errors win even if the loaded snapshot has duplicate row IDs.
    [Fact]
    public void SaveValidatesLineFieldsBeforeRuleSnapshot()
    {
        var duplicateIds = new[] { Rule(4), Rule(4) };
        foreach (var (line, field) in new (SalesPriceLineRequest, string)[]
        {
            (new(-1m, 0m, null, null, null), "unitPrice"),
            (new(100m, 100.0001m, null, null, null), "discountRate"),
            (new(100m, 0m, 0, 3, null), "pricingRuleId"),
        })
        {
            var error = FluentActions.Invoking(() => SalesPricingRules.DecideForSave(
                Lookup(), duplicateIds, line, false, "lines[2]"))
                .Should().Throw<RequestValidationException>().Which;
            error.Errors.Should().ContainKey($"lines[2].{field}");
        }

        FluentActions.Invoking(() => SalesPricingRules.DecideForSave(
            Lookup(), duplicateIds, new(100m, 0m, null, null, null), false, "lines[2]"))
            .Should().Throw<RequestValidationException>().Which.Errors.Should().ContainKey("rules");

        SalesPricingRules.DecideForSave(Lookup(), [Rule(4)],
            new(100m, 0m, 4, 1, null), false, "lines[2]")
            .Should().Be(new SalesPriceDecision(4, 1, 100m, 10m, 100m, 0m, false, null));
    }

    // SD-PRICE-U24～U26: a corrupt loaded row must not become an advisory or saved price.
    [Fact]
    public void ValidBoundarySnapshotCanBeQuotedAndSaved()
    {
        var rule = Rule(4, from: Date, to: Date, price: 0.000001m,
            ceiling: 0.0001m, version: 1);
        SalesPricingRules.Quote(Lookup(), [rule]).UnitPrice.Should().Be(0.000001m);
        SalesPricingRules.DecideForSave(Lookup(), [rule],
            new(0.000001m, 0.0001m, 4, 1, null), false, "lines[0]")
            .IsOverride.Should().BeFalse();
        var zero = rule with { UnitPrice = 0m, DiscountCeiling = 0m };
        SalesPricingRules.Quote(Lookup(), [zero]).UnitPrice.Should().Be(0m);
        SalesPricingRules.DecideForSave(Lookup(), [zero],
            new(0m, 0m, 4, 1, null), false, "lines[0]").IsOverride.Should().BeFalse();
    }

    [Fact]
    public void InvalidLoadedRuleNeverProducesQuoteOrSaveDecision()
    {
        var valid = Rule(4);
        var invalid = new[]
        {
            valid with { Version = 0 }, valid with { CompanyId = 0 },
            valid with { ItemId = 0 }, valid with { CustomerId = 0 },
            valid with { CurrencyCode = "twd" }, valid with { ValidFrom = default },
            valid with { ValidTo = default },
            valid with { ValidFrom = new DateOnly(2029, 1, 1) },
            valid with { UnitPrice = -1m }, valid with { UnitPrice = 1.0000001m },
            valid with { UnitPrice = 10_000_000_000_000m },
            valid with { DiscountCeiling = -0.0001m },
            valid with { DiscountCeiling = 100.0001m },
            valid with { DiscountCeiling = 1.00001m },
        };
        foreach (var rule in invalid)
        {
            foreach (Action action in new Action[]
            {
                () => SalesPricingRules.Select(Lookup(), [rule]),
                () => SalesPricingRules.Quote(Lookup(), [rule]),
                () => SalesPricingRules.DecideForSave(Lookup(), [rule],
                    new(100m, 0m, null, null, "approved"), true, "lines[0]"),
            })
                FluentActions.Invoking(action).Should().Throw<RequestValidationException>()
                    .Which.Errors.Should().ContainKey("rules");
        }
    }

    [Fact]
    public void InvalidNondecidingOrInactiveSnapshotCannotBeHiddenByOverride()
    {
        var invalid = Rule(8, customerId: 51, version: 0, active: false, deleted: true);
        FluentActions.Invoking(() => SalesPricingRules.Quote(Lookup(), [Rule(4), invalid]))
            .Should().Throw<RequestValidationException>().Which.Errors.Should().ContainKey("rules");
        FluentActions.Invoking(() => SalesPricingRules.DecideForSave(Lookup(), [Rule(4), invalid],
            new(100m, 0m, null, null, "approved"), true, "lines[0]"))
            .Should().Throw<RequestValidationException>().Which.Errors.Should().ContainKey("rules");
        FluentActions.Invoking(() => SalesPricingRules.DecideForSave(Lookup(), [invalid],
            new(-1m, 0m, null, null, null), true, "lines[0]"))
            .Should().Throw<RequestValidationException>().Which.Errors.Should().ContainKey("lines[0].unitPrice");
    }
}
