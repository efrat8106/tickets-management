# Issues Log

Problems found during development, how they were detected, and how they were resolved.

Type: **Bug** – a defect in code/configuration that had to be fixed · **Environment** – missing/misconfigured tooling on
the dev machine · **Expected** – an observation that looks like a problem but is the correct behavior at that stage
(re-checked later).

| # | Task | Type | Issue | How detected | Resolution |
|---|---|---|---|---|---|
| 1 | T-02 | Environment | .NET 8 SDK/runtime not installed (only .NET 6, 7, 10). | `dotnet --list-sdks` / `--list-runtimes` | Switched target to .NET 10 (current LTS; .NET 8 support ends Nov 2026). Documented in PLAN.md. |
| 2 | T-02 | Environment | Docker daemon not running. | `docker info` failed to connect | Started Docker Desktop. README lists it as a prerequisite. |
| 3 | T-03 | Bug | Transitive package `System.Security.Cryptography.Pkcs` 9.0.11 (via Microsoft.Data.SqlClient) has known moderate/high vulnerabilities; build failed (NU1902/NU1903 as errors). | `dotnet build` with warnings-as-errors | Pinned `System.Security.Cryptography.Pkcs` 10.0.14 explicitly in Infrastructure instead of suppressing the warning. |
| 4 | T-03 | Bug | `docker compose up` failed: "project name must not be empty" – Compose derives the project name from the folder name, which is Hebrew. | Running `docker compose up -d` | Added an explicit `name: tickets` to `docker-compose.yml`, so it works regardless of the clone folder name. |
| 5 | T-03 | Bug | `AddValidatorsFromAssembly` not found in Application. | `dotnet build` | Moved `FluentValidation.DependencyInjectionExtensions` to the Application project (where validators are registered). |
| 6 | T-04 | Expected | `/health` returns 503 Unhealthy. | Manual probe of the running API | Correct: the `TicketsDb` database is only created by the migration in T-07. Re-check after T-07 (must be 200 Healthy). |
| 7 | T-04 | Expected | EF warning at startup: no `IEntityTypeConfiguration` found in Infrastructure. | API console log | Correct until entity configurations are added in T-07. Re-check after T-07. |
