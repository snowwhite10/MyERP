using System.Text;
using MyERP.DocGen.Consistency;
using MyERP.DocGen.Excel;
using MyERP.DocGen.Generation;
using MyERP.DocGen.Schema;
using MyERP.DocGen.Sources;
using MyERP.Shared.Time;

namespace MyERP.DocGen;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Contains("--help", StringComparer.Ordinal))
        {
            Console.WriteLine("Usage: dotnet run --project tools/docgen/MyERP.DocGen -- [--root PATH] [--connection CONNECTION_STRING]");
            return 0;
        }

        string? root = null;
        string? connection = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (i + 1 >= args.Length)
            {
                Console.Error.WriteLine($"Missing value for {args[i]}.");
                return 2;
            }

            switch (args[i])
            {
                case "--root": root = args[++i]; break;
                case "--connection": connection = args[++i]; break;
                default:
                    Console.Error.WriteLine($"Unknown option: {args[i]}");
                    return 2;
            }
        }

        try
        {
            var layout = root is null ? RepositoryLayout.Discover(Environment.CurrentDirectory) : new RepositoryLayout(root);
            var requirements = new RequirementSourceLoader(layout).Load();
            var schema = connection is null
                ? DatabaseSchema.Unavailable("no --connection supplied")
                : await new SqlServerSchemaSource(connection).ReadAsync(CancellationToken.None);
            var clock = new SystemClock(TimeProvider.System, TimeSpan.FromHours(8));
            var context = new GenerationContext(layout, requirements, schema, clock.Today);
            Directory.CreateDirectory(layout.OutputDirectory);

            var srsPath = Path.Combine(layout.OutputDirectory, SrsGenerator.FileName);
            DocumentResult srs;
            using (var stream = File.Create(srsPath))
            {
                srs = SrsGenerator.Generate(context, stream, srsPath);
            }

            var sdsPath = Path.Combine(layout.OutputDirectory, SdsGenerator.FileName);
            DocumentResult sds;
            using (var stream = File.Create(sdsPath))
            {
                sds = SdsGenerator.Generate(context, stream, sdsPath);
            }

            var workbookPath = Path.Combine(layout.OutputDirectory, RequirementsWorkbook.FileName);
            using (var stream = File.Create(workbookPath))
            {
                RequirementsWorkbook.Generate(context, stream);
            }

            var issues = ConsistencyChecker.Check(new ConsistencyInput(requirements, schema, srs, sds));
            var reportPath = Path.Combine(layout.OutputDirectory, ConsistencyReport.FileName);
            File.WriteAllText(reportPath, ConsistencyReport.Render(requirements, issues, clock.Today, schema.Source), new UTF8Encoding(false));
            Console.WriteLine($"Generated SRS, SDS, workbook and consistency report in {layout.Relative(layout.OutputDirectory)}; errors: {issues.Count(i => i.Severity == IssueSeverity.Error)}, warnings: {issues.Count(i => i.Severity == IssueSeverity.Warning)}.");
            return issues.Any(i => i.Severity == IssueSeverity.Error) ? 1 : 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or Microsoft.Data.SqlClient.SqlException)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }
}
