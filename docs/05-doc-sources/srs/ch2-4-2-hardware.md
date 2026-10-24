The following figures are planning assumptions for a single-user development VM. They are not a measured production sizing result. Concurrency, database volume and report workload must be measured before production sizing.

| Hardware | Planning baseline for local development |
|---|---|
| Processor | 64-bit processor with four logical cores. |
| Memory | 16 GB RAM for the API, SQL Server, Redis, Angular build and test tools; larger test runs may require more memory or controlled parallelism. |
| Storage | 40 GB free space for source, build outputs, local databases and test artifacts, with additional space for retained data and logs. |
| Network | Loopback connectivity between browser, API, SQL Server and Redis; network access for initial package and browser installation. |

The local SQL Server and Redis services must be available for integration tests and real API acceptance. When a dependency is stopped, documentation generation can run without schema verification, but the result does not establish database consistency.
