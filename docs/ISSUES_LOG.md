# Issues Log

Problems found during development, how they were detected, and how they were resolved.

| # | Task | Issue | How detected | Resolution |
|---|---|---|---|---|
| 1 | T-02 | .NET 8 SDK/runtime not installed (only .NET 6, 7, 10). | `dotnet --list-sdks` / `--list-runtimes` | Switched target to .NET 10 (current LTS; .NET 8 support ends Nov 2026). Documented in PLAN.md. |
| 2 | T-02 | Docker daemon not running. | `docker info` failed to connect | Started Docker Desktop. README will list it as a prerequisite. |
| 3 | T-03 | Transitive package `System.Security.Cryptography.Pkcs` 9.0.11 (via Microsoft.Data.SqlClient) has known moderate/high vulnerabilities; build failed (NU1902/NU1903 as errors). | `dotnet build` with warnings-as-errors | Pinned `System.Security.Cryptography.Pkcs` 10.0.14 explicitly in Infrastructure instead of suppressing the warning. |
| 4 | T-03 | `docker compose up` failed: "project name must not be empty" – Compose derives the project name from the folder name, which is Hebrew. | Running `docker compose up -d` | Added an explicit `name: tickets` to `docker-compose.yml`, so it works regardless of the clone folder name. |
| 5 | T-03 | `AddValidatorsFromAssembly` not found in Application. | `dotnet build` | Moved `FluentValidation.DependencyInjectionExtensions` to the Application project (where validators are registered). |
| 6 | T-04 | `/health` returns 503 Unhealthy. | Manual probe of the running API | Expected at this stage: the `TicketsDb` database is only created by the migration in T-07. To be re-checked (must be 200 Healthy) after T-07. |
| 7 | T-04 | EF warning at startup: no `IEntityTypeConfiguration` found in Infrastructure. | API console log | Expected until entity configurations are added in T-07; to be re-checked then. |
