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
| 6 | T-04 | Expected | `/health` returns 503 Unhealthy. | Manual probe of the running API | Correct: the `TicketsDb` database is only created by the migration in T-07. **Re-checked in T-07: 200 Healthy.** |
| 7 | T-04 | Expected | EF warning at startup: no `IEntityTypeConfiguration` found in Infrastructure. | API console log | Correct until entity configurations are added in T-07. **Re-checked in T-07: warning gone.** |
| 8 | T-07 | Bug | After adding the migration, the API started but only `__EFMigrationsHistory` was created – no tables. | Inspected `sys.tables` after startup instead of trusting the 200 health check | Process error: the API was started with `--no-build`, so the running assembly did not contain the new migration. Rebuilt and restarted; migration applied. Lesson: always rebuild after `ef migrations add` and verify the schema itself. |
| 9 | T-10 | Bug | Unit test `Undefined_enum_values_are_invalid` failed: expected key `status[0]`, got `Status[0]`. | `dotnet test` | Test bug, not a validator bug: camelCase keys are configured globally in `AddApplication` (DI), which a standalone unit test does not run. The unit test now checks the error case-insensitively; camelCase keys in the HTTP response are verified by the integration tests (T-14). |
| 10 | T-11 | Bug | Validation error keys came back PascalCase (`SortBy`, `Status`) – FluentValidation's global `PropertyNameResolver` was not applied, and model-binding errors never go through FluentValidation anyway. | Manual HTTP check of 400 responses | Key normalization moved to one place in the API layer (`ValidationProblemResults`), used by both the FluentValidation filter and `InvalidModelStateResponseFactory`, so both error sources produce identical camelCase keys. |
| 11 | T-11 | Bug | One validation message was returned in Hebrew (`'pageSize' חייב להיות בין 1 לבין 100`) – FluentValidation localizes by the server OS culture. | Manual HTTP check | Disabled FluentValidation localization: API messages are always English, independent of the host machine. |
| 12 | T-11 | Bug | 400 responses had `Content-Type: application/json` instead of `application/problem+json`. | Manual HTTP check | `[Produces("application/json")]` on the controller overrode the content type of error results; removed it. |
| 13 | T-11 | Bug | Incorrect assumption from T-10: "model binding accepts `status=7`". In fact ASP.NET Core's enum binder rejects undefined values (`The value '7' is invalid.`). | Manual HTTP check | Corrected the code comment and AI log. The validator rule is kept as defense in depth (validator correct on its own). |
