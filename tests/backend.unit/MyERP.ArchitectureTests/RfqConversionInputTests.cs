using MyERP.Modules.Purchasing.Application;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

public sealed class RfqConversionInputTests
{
    [Fact]
    public void ValidSelectionPreservesExplicitAwardOrder()
    {
        var input = new RfqConversionInput(2, 13, [7, 3]);
        input.Validate();
        input.AwardIds.Should().Equal(7, 3);
    }

    [Theory]
    [InlineData(0, 13L, new long[] { 7 }, "version")]
    [InlineData(2, 0L, new long[] { 7 }, "supplierId")]
    [InlineData(2, 13L, new long[] { }, "awardIds")]
    [InlineData(2, 13L, new long[] { 0 }, "awardIds")]
    [InlineData(2, 13L, new long[] { 7, 7 }, "awardIds")]
    public void InvalidSelectionIsRejectedBeforeDatabaseAccess(int version, long supplierId,
        long[] awardIds, string field)
    {
        var input = new RfqConversionInput(version, supplierId, awardIds);
        input.Invoking(x => x.Validate()).Should().Throw<RequestValidationException>()
            .Which.Errors.Should().ContainKey(field);
    }

    [Fact]
    public void MissingAwardCollectionIsRejected()
    {
        var input = new RfqConversionInput(2, 13, null);
        input.Invoking(x => x.Validate()).Should().Throw<RequestValidationException>()
            .Which.Errors.Should().ContainKey("awardIds");
    }

    [Fact]
    public void ThousandDifferentAwardsAreAccepted()
    {
        var input = new RfqConversionInput(2, 13,
            Enumerable.Range(1, 1000).Select(x => (long)x).ToArray());
        input.Validate();
    }

    [Fact]
    public void MoreThanThousandAwardsIsRejected()
    {
        var input = new RfqConversionInput(2, 13, Enumerable.Range(1, 1001).Select(x => (long)x).ToArray());
        input.Invoking(x => x.Validate()).Should().Throw<RequestValidationException>()
            .Which.Errors.Should().ContainKey("awardIds");
    }
}
