using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Finance_GlPostedImmutable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TRIGGER dbo.tr_gl_source_key_immutable ON dbo.gl_source_key
                AFTER UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM deleted)
                        THROW 51023, 'Committed GL source keys are immutable.', 1;
                END
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER dbo.tr_gl_journal_immutable ON dbo.gl_journal
                AFTER UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM deleted)
                        THROW 51023, 'Posted GL journals are immutable.', 1;
                END
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER dbo.tr_gl_journal_line_immutable ON dbo.gl_journal_line
                AFTER UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM deleted)
                        THROW 51023, 'Posted GL journal lines are immutable.', 1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER dbo.tr_gl_journal_line_immutable;");
            migrationBuilder.Sql("DROP TRIGGER dbo.tr_gl_journal_immutable;");
            migrationBuilder.Sql("DROP TRIGGER dbo.tr_gl_source_key_immutable;");
        }
    }
}
