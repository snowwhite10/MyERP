using MyERP.Modules.Finance.Application;
using MyERP.Modules.MasterData.Contracts;

namespace MyERP.ArchitectureTests;

public sealed class GlAccountSnapshotRulesTests
{
    private static AccountInfo Account(long id = 7, long company = 3, int version = 2,
        string code = "1002", string name = "Bank", bool active = true, bool postable = true) =>
        new(id, company, version, code, name, "1", AccountType.Asset,
            BalanceDirection.Debit, active, postable);

    [Fact]
    public void MatchingPostableAccountProducesImmutableScalarSnapshot()
    {
        var result = GlAccountSnapshotRules.Validate(3, 7, "1002", 4, 4, Account());
        Assert.Equal(GlAccountSnapshotStatus.Allowed, result.Status);
        Assert.Equal(new GlAccountSnapshot(3, 7, "1002", "Bank", 2, 4), result.Snapshot);
    }

    [Theory]
    [InlineData(0, 7, "1002", 4, 4, GlAccountSnapshotStatus.InvalidInput)]
    [InlineData(3, 0, "1002", 4, 4, GlAccountSnapshotStatus.InvalidInput)]
    [InlineData(3, 7, "", 4, 4, GlAccountSnapshotStatus.InvalidInput)]
    [InlineData(3, 7, "1002", 0, 4, GlAccountSnapshotStatus.InvalidInput)]
    [InlineData(3, 7, "1002", 4, 5, GlAccountSnapshotStatus.MappingChanged)]
    public void RejectsInvalidInputOrMappingBeforeAccount(long company, long id, string code,
        int expectedMapping, int currentMapping, GlAccountSnapshotStatus status)
    {
        var result = GlAccountSnapshotRules.Validate(company, id, code, expectedMapping, currentMapping, Account());
        Assert.Equal(status, result.Status);
        Assert.Null(result.Snapshot);
    }

    [Fact]
    public void RejectsMissingForeignOrStaleAccountWithoutSnapshot()
    {
        Assert.Equal(GlAccountSnapshotStatus.Missing, GlAccountSnapshotRules.Validate(3, 7, "1002", 4, 4, null).Status);
        Assert.Equal(GlAccountSnapshotStatus.WrongCompany, GlAccountSnapshotRules.Validate(3, 7, "1002", 4, 4, Account(company: 8)).Status);
        Assert.Equal(GlAccountSnapshotStatus.AccountChanged, GlAccountSnapshotRules.Validate(3, 7, "1002", 4, 4, Account(id: 9)).Status);
        Assert.Equal(GlAccountSnapshotStatus.AccountChanged, GlAccountSnapshotRules.Validate(3, 7, "1002", 4, 4, Account(code: "1003")).Status);
    }

    [Fact]
    public void RejectsInactiveNonleafAndMalformedReturnedSnapshot()
    {
        Assert.Equal(GlAccountSnapshotStatus.Inactive, GlAccountSnapshotRules.Validate(3, 7, "1002", 4, 4, Account(active: false)).Status);
        Assert.Equal(GlAccountSnapshotStatus.NotPostable, GlAccountSnapshotRules.Validate(3, 7, "1002", 4, 4, Account(postable: false)).Status);
        Assert.Equal(GlAccountSnapshotStatus.InvalidSnapshot, GlAccountSnapshotRules.Validate(3, 7, "1002", 4, 4, Account(version: 0)).Status);
        Assert.Equal(GlAccountSnapshotStatus.InvalidSnapshot, GlAccountSnapshotRules.Validate(3, 7, "1002", 4, 4, Account(name: "")).Status);
        Assert.Equal(GlAccountSnapshotStatus.InvalidSnapshot, GlAccountSnapshotRules.Validate(3, 7, "1002", 4, 4, Account(code: "1002", name: new string('X', 201))).Status);
    }

    [Fact]
    public void InputCodeMustFitMdAndGlColumnsWithoutNormalization()
    {
        Assert.Equal(GlAccountSnapshotStatus.Allowed,
            GlAccountSnapshotRules.Validate(3, 7, "1002", 4, 4, Account()).Status);
        foreach (var invalid in new[] { " 1002", "1002 ", "A002", new string('1', 31) })
            Assert.Equal(GlAccountSnapshotStatus.InvalidInput,
                GlAccountSnapshotRules.Validate(3, 7, invalid, 4, 4, Account()).Status);
    }

    [Fact]
    public void MappingConflictWinsOverMissingAccountAndNoResultLeaksSnapshot()
    {
        var result = GlAccountSnapshotRules.Validate(3, 7, "1002", 4, 5, null);
        Assert.Equal(GlAccountSnapshotStatus.MappingChanged, result.Status);
        Assert.Null(result.Snapshot);
    }
}
