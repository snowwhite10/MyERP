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
}
