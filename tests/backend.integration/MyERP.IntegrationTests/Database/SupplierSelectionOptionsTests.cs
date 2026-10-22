using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Domain;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;

namespace MyERP.IntegrationTests.Database;

public sealed class SupplierSelectionOptionsTests(TestDatabase database)
{
    private MyErpDbContext Context(long companyId) => new(
        new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(new AuditStamper(
                new FixedClock(new DateTime(2026, 10, 23)), new FixedUser(7), new FixedCompany(companyId)))).Options,
        [new SupplierDbModel()], new FixedCompany(companyId));

    [Fact]
    public async Task ApprovedOptionsAreCompanyScopedAndSavedOptionsExplainInactiveReferences()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = TestCompanies.Next();
        var otherCompanyId = TestCompanies.Next();
        await using var db = Context(companyId);
        await using var foreign = Context(otherCompanyId);
        var approved = new Supplier { CompanyId = companyId, Code = $"A{companyId}", Name = "Approved",
            ReviewStatus = PartnerReviewStatus.Approved };
        var pending = new Supplier { CompanyId = companyId, Code = $"B{companyId}", Name = "Pending" };
        var inactive = new Supplier { CompanyId = companyId, Code = $"C{companyId}", Name = "Inactive",
            ReviewStatus = PartnerReviewStatus.Approved, IsActive = false };
        var deleted = new Supplier { CompanyId = companyId, Code = $"D{companyId}", Name = "Deleted",
            ReviewStatus = PartnerReviewStatus.Approved, IsDeleted = true };
        var other = new Supplier { CompanyId = otherCompanyId, Code = $"E{companyId}", Name = "Other",
            ReviewStatus = PartnerReviewStatus.Approved };
        db.AddRange(approved, pending, inactive, deleted);
        foreign.Add(other);
        await db.SaveChangesAsync(token);
        await foreign.SaveChangesAsync(token);
        var options = new SupplierSelectionOptions(db);
        var result = await options.ApprovedAsync(new PagedRequest { Keyword = "approved" }, token);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal(approved.Id, Assert.Single(result.Items).Id);
        var saved = await options.SavedAsync([approved.Id, pending.Id, inactive.Id, deleted.Id, other.Id], token);
        Assert.Equal(3, saved.Count);
        Assert.False(saved.Single(x => x.Id == pending.Id).IsApproved);
        Assert.False(saved.Single(x => x.Id == inactive.Id).IsActive);
        await Assert.ThrowsAsync<RequestValidationException>(() => options.ApprovedAsync(
            new PagedRequest { Sort = "name" }, token));
    }
}
