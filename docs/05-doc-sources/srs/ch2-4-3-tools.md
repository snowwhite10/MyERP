The project uses the following development and verification tools. Package versions are pinned by the repository's version files and architecture decisions rather than by this narrative.

| Tool | Purpose |
|---|---|
| .NET 10 SDK and C# | Build the ASP.NET Core Web API, modules, migrations and document generator. |
| Angular 22, TypeScript, Node 24.21.0 and ng-zorro-antd | Build the browser client and its user interface; the repository's frontend launcher selects the portable Node version. |
| AntV G2 | Render charts in dashboards and reports. |
| SQL Server 2022 and EF Core | Store transactional data and apply versioned schema migrations with Windows integrated authentication in local development. |
| Redis-compatible service | Store refresh tokens and support shared cache behavior. |
| Git and PowerShell | Version source and run development, verification and document generation scripts. |
| xUnit, Angular tests and Playwright | Run backend, frontend and browser tests. |
| OpenXML-based document generator | Generate the SRS, SDS and requirements workbook from controlled sources. |

Serilog writes application logs; Application Insights is optional and requires an explicit connection string. Docker is not part of the development or delivery path.
