using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using MyERP.Migrations;

namespace MyERP.IntegrationTests.Database;

/// <summary>SP 部署機制（不連線）：嵌入資源命名、讀取、migration 操作。</summary>
public sealed class ProcedureScriptsTests
{
    private static MigrationBuilder NewBuilder() => new("Microsoft.EntityFrameworkCore.SqlServer");

    [Fact]
    public void All_ListsEmbeddedProceduresByPrefixAndName()
    {
        ProcedureScripts.All().Should().Contain("sys/usp_sys_database_info");
    }

    [Fact]
    public void All_FollowNamingConvention_AndCreateOrAlter()
    {
        foreach (var path in ProcedureScripts.All())
        {
            var parts = path.Split('/');
            parts.Should().HaveCount(2, $"{path} must be database/procedures/{{prefix}}/usp_{{prefix}}_*.sql");
            parts[1].Should().StartWith($"usp_{parts[0]}_");

            var script = ProcedureScripts.Read(path);
            script.Should().Contain($"CREATE OR ALTER PROCEDURE dbo.{parts[1]}");
            script.Should().Contain("SET NOCOUNT ON");
            script.Should().Contain("@company_id");
        }
    }

    [Theory]
    [InlineData("sys/usp_sys_database_info")]
    [InlineData(@"sys\usp_sys_database_info")]
    public void Read_ReturnsScriptContent(string path)
    {
        ProcedureScripts.Read(path).Should().Contain("dbo.usp_sys_database_info");
    }

    [Fact]
    public void Read_UnknownScript_Throws()
    {
        var act = () => ProcedureScripts.Read("sys/usp_sys_missing");

        act.Should().Throw<InvalidOperationException>().WithMessage("*procedures/sys/usp_sys_missing.sql*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Read_BlankPath_Throws(string path)
    {
        var act = () => ProcedureScripts.Read(path);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CreateOrAlterProcedure_AddsSqlOperationWithScript()
    {
        var builder = NewBuilder();

        builder.CreateOrAlterProcedure("sys/usp_sys_database_info");

        builder.Operations.Should().ContainSingle().Which.Should().BeOfType<SqlOperation>()
            .Which.Sql.Should().Be(ProcedureScripts.Read("sys/usp_sys_database_info"));
    }

    [Fact]
    public void DropProcedure_AddsDropIfExists()
    {
        var builder = NewBuilder();

        builder.DropProcedure("sys/usp_sys_database_info");

        builder.Operations.Should().ContainSingle().Which.Should().BeOfType<SqlOperation>()
            .Which.Sql.Should().Be("DROP PROCEDURE IF EXISTS dbo.usp_sys_database_info;");
    }

    [Theory]
    [InlineData("sys/usp_x; DROP TABLE t")]
    [InlineData("sys/usp-x")]
    [InlineData("sys/")]
    public void DropProcedure_InvalidName_Throws(string path)
    {
        var act = () => NewBuilder().DropProcedure(path);

        act.Should().Throw<ArgumentException>();
    }
}
