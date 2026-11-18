using System.Text.Json;
using MyERP.Modules.Sales.Application;
using MyERP.Modules.Sales.Domain;

namespace MyERP.ArchitectureTests;

public sealed class SalesOrderStatusJsonTests
{
    [Theory]
    [InlineData(SalesOrderStatus.Draft, "\"Draft\"")]
    [InlineData(SalesOrderStatus.Pending, "\"Pending\"")]
    [InlineData(SalesOrderStatus.Rejected, "\"Rejected\"")]
    [InlineData(SalesOrderStatus.Approved, "\"Approved\"")]
    [InlineData(SalesOrderStatus.Cancelled, "\"Cancelled\"")]
    public void SerializesNamedStatus(SalesOrderStatus status, string expected)
    {
        JsonSerializer.Serialize(status).Should().Be(expected);
        JsonSerializer.Deserialize<SalesOrderStatus>(expected).Should().Be(status);
    }

    [Fact]
    public void RejectsNumericStatusInJson()
    {
        Action read = () => JsonSerializer.Deserialize<SalesOrderStatus>("0");
        read.Should().Throw<JsonException>();
    }

    [Fact]
    public void SummaryAndDetailExposeNamedStatus()
    {
        var summary = new SalesOrderSummary(1, "SO001", 2, "Customer",
            new DateOnly(2026, 11, 18), SalesOrderStatus.Draft, 12m, 1);
        var detail = new SalesOrderDetail(1, "SO001", 2, "C", "Customer",
            new DateOnly(2026, 11, 18), "TWD", "TWD", 1m,
            SalesOrderStatus.Pending, null, 10m, 2m, 12m, 10m, 2m, 12m,
            null, 1, []);

        JsonDocument.Parse(JsonSerializer.Serialize(summary)).RootElement
            .GetProperty("Status").GetString().Should().Be("Draft");
        JsonDocument.Parse(JsonSerializer.Serialize(detail)).RootElement
            .GetProperty("Status").GetString().Should().Be("Pending");
    }
}
