using MyERP.DocGen.Schema;

namespace MyERP.DocGen.Tests;

public sealed class SqlServerSchemaSourceTests
{
    [Fact]
    public async Task RealSchema_ReadsDeliveryTablesAndKeysWithinCommandTimeout()
    {
        var connectionString = Environment.GetEnvironmentVariable("MYERP_DOCGEN_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return; // CI has no SQL Server; the local real-SQL gate sets this variable.
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var schema = await new SqlServerSchemaSource(connectionString).ReadAsync(timeout.Token);

        schema.IsAvailable.Should().BeTrue();
        var delivery = schema.Tables.Single(t => t.Name == "sal_delivery");
        var lines = schema.Tables.Single(t => t.Name == "sal_delivery_line");
        delivery.Columns.Single(c => c.Name == "id").IsPrimaryKey.Should().BeTrue();
        lines.Columns.Single(c => c.Name == "id").IsPrimaryKey.Should().BeTrue();
        lines.Columns.Single(c => c.Name == "delivery_id").IsForeignKey.Should().BeTrue();
        lines.Columns.Single(c => c.Name == "quantity").SizeText.Should().Be("(19,6)");
        schema.Tables.Should().NotContain(t => t.Name == "__EFMigrationsHistory");
    }
}
