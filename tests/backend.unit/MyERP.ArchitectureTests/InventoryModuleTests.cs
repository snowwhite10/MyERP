using MyERP.ArchitectureTests.Infrastructure;
using MyERP.Modules.Inventory;
using MyERP.Modules.Inventory.Contracts;
using MyERP.Modules.Inventory.Domain;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Security;

namespace MyERP.ArchitectureTests;

public sealed class InventoryModuleTests
{
    [Fact]
    public void InventoryModuleHasIsolatedContractsAndPermissionCatalog()
    {
        var names = SolutionAssemblies.All.Select(a => a.GetName().Name).ToList();
        names.Should().Contain(["MyERP.Modules.Inventory", "MyERP.Modules.Inventory.Contracts"]);
        var module = new InventoryModule();
        module.Code.Should().Be("INV");
        var catalog = new PermissionCatalog([new InventoryPermissions()]);
        foreach (var permission in new InventoryPermissions().Permissions)
        {
            permission.Module.Should().Be("INV");
            permission.Code.Should().StartWith("INV.");
            catalog.Contains(permission.Code).Should().BeTrue();
        }
    }

    [Fact]
    public void InventoryDocumentRejectsOtherCompanyAndInvalidTransitions()
    {
        var document = new InventoryDocument(10, "OTHER_RECEIPT");
        var act = () => document.Submit(11);
        act.Should().Throw<InvalidOperationException>();
        document.Status.Should().Be(InventoryDocumentStatus.Draft);

        document.Submit(10);
        document.Status.Should().Be(InventoryDocumentStatus.PendingApproval);
        document.Approve(10);
        document.Status.Should().Be(InventoryDocumentStatus.Approved);
        var again = () => document.Approve(10);
        again.Should().Throw<InvalidOperationException>();
        var otherCompanyPost = () => document.Post(11);
        otherCompanyPost.Should().Throw<InvalidOperationException>();
        document.Post(10);
        document.Status.Should().Be(InventoryDocumentStatus.Posted);
        var postedVoid = () => document.Void(10);
        postedVoid.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RejectedDocumentCannotPostAndDraftCanBeVoided()
    {
        var rejected = new InventoryDocument(10, "OTHER_ISSUE");
        rejected.Submit(10);
        rejected.Reject(10);
        rejected.Status.Should().Be(InventoryDocumentStatus.Rejected);
        var post = () => rejected.Post(10);
        post.Should().Throw<InvalidOperationException>();

        var draft = new InventoryDocument(10, "OTHER_ISSUE");
        draft.Void(10);
        draft.Status.Should().Be(InventoryDocumentStatus.Voided);
    }
}
