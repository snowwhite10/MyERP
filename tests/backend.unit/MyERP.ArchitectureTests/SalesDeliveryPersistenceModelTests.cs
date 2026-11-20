using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using MyERP.Modules.Sales.Domain;
using MyERP.Modules.Sales.Infrastructure;

namespace MyERP.ArchitectureTests;

public sealed class SalesDeliveryPersistenceModelTests
{
    [Fact]
    public void DeliveryOwnsSavedLinesWithoutCrossModuleForeignKeys()
    {
        var builder = new ModelBuilder(new ConventionSet());
        new SalesDeliveryDbModel().Configure(builder);
        var model = builder.FinalizeModel();
        var header = model.FindEntityType(typeof(SalesDelivery))!;
        var line = model.FindEntityType(typeof(SalesDeliveryLine))!;

        header.GetTableName().Should().Be("sal_delivery");
        line.GetTableName().Should().Be("sal_delivery_line");
        line.GetForeignKeys().Should().ContainSingle()
            .Which.PrincipalEntityType.Should().Be(header);
        line.GetForeignKeys().Single().Properties.Select(x => x.Name)
            .Should().Equal(nameof(SalesDeliveryLine.CompanyId), nameof(SalesDeliveryLine.DeliveryId));
        line.FindProperty(nameof(SalesDeliveryLine.Quantity))!.GetPrecision().Should().Be(19);
        line.FindProperty(nameof(SalesDeliveryLine.Quantity))!.GetScale().Should().Be(6);
        header.GetIndexes().Should().Contain(x => x.IsUnique &&
            x.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(SalesDelivery.CompanyId), nameof(SalesDelivery.DocumentNo) }));
    }

    [Fact]
    public void PostedLineKeepsItsInventoryMovementAsAUniqueScalarReference()
    {
        // WU-SD-004b2 (ADR-146): the INV movement ID is a nullable scalar, unique per company once set, with no FK to INV.
        var builder = new ModelBuilder(new ConventionSet());
        new SalesDeliveryDbModel().Configure(builder);
        var line = builder.FinalizeModel().FindEntityType(typeof(SalesDeliveryLine))!;

        line.FindProperty(nameof(SalesDeliveryLine.InvMovementId))!.IsNullable.Should().BeTrue();
        var index = line.GetIndexes().Single(x => x.Properties.Any(p => p.Name == nameof(SalesDeliveryLine.InvMovementId)));
        index.IsUnique.Should().BeTrue();
        index.Properties.Select(p => p.Name).Should()
            .Equal(nameof(SalesDeliveryLine.CompanyId), nameof(SalesDeliveryLine.InvMovementId));
        index.GetFilter().Should().Be("[inv_movement_id] IS NOT NULL");
        line.GetCheckConstraints().Select(x => x.Name).Should().Contain("ck_sal_delivery_line_inv_movement");
        line.GetForeignKeys().Should().ContainSingle();
    }
}
