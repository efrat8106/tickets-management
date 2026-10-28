# Work Plan – Ticket Management System

This document translates the assignment specification into a development plan: epics, tasks, deliverables,
acceptance criteria, estimates, dependencies, execution order and traceability back to each requirement.

Task IDs follow the execution order. Status legend: `[ ]` not started · `[x]` done.

## 1. Requirement IDs

| ID  | Requirement (from the specification)                                   |
|-----|------------------------------------------------------------------------|
| R1  | Data retrieval: server-side paging, combined filters, search, sort, aggregations, validation, CancellationToken, DTOs |
| R2  | Status update & optimistic concurrency (404, transition validation, 409, RowVersion/UpdatedAt, no lost update) |
| R3  | Status change history (audit) + history endpoint                       |
| R4  | Bulk status update (≤100 items, defined partial-failure behavior)      |
| R5  | Angular client (server-side ops, debounce, request cancellation, UI states, 409, aggregations, history, separation of concerns) |
| R6  | Performance (2 operations measured on 100k rows, execution plans, indexes, bottleneck) |
| R7  | Cache (one aggregate, expiration, invalidation, justification, scale-out) |
| R8  | Automated tests (≥4 meaningful, ≥1 integration, validation, concurrency) |
| R9  | Work planning & task breakdown (this document)                         |
| R10 | Code quality (structure, DI lifetimes, async, global error handling, validation, logging, no over-engineering) |
| R11 | Solution documentation (run instructions, versions, structure, DB, concurrency, bulk, cache, decisions, limitation, AI usage) |
| DATA| ≥100,000 records, reproducible seed                                    |

## 2. Technology baseline

| Area | Choice | Alternative considered |
|---|---|---|
| Backend | .NET 10 (LTS), ASP.NET Core Web API with Controllers | Minimal APIs. .NET 8 was the initial target, but it is not installed on the dev machine and reaches end of support in Nov 2026; .NET 10 is the current LTS. |
| ORM | EF Core 10 + SQL Server provider | Dapper for aggregations – not needed, EF translates `GroupBy` to SQL. |
| DB | SQL Server 2022 (Docker Compose) | MongoDB |
| Frontend | Angular 18, standalone components, Signals for view state, RxJS for data streams | NgRx – over-engineering for this size. |
| Tests | xUnit + FluentAssertions 7.2 + WebApplicationFactory + Testcontainers (MsSql) | SQLite / InMemory – no real `rowversion` semantics. FluentAssertions is pinned to 7.x (Apache 2.0); version 8 moved to a commercial license. |
| Cache | `IMemoryCache` behind `ISummaryCache` | Redis – documented for scale-out. |
| Logging | Serilog (console) via `ILogger<T>` | – |
| Validation | FluentValidation (single approach everywhere) | DataAnnotations |

## 3. Design decisions made during planning

- **Audit naming:** `TicketStatusHistory.RequestId` is the **FK to the ticket** ("request" = פנייה, as named in the
  specification). The HTTP correlation id (from the `X-Correlation-Id` header or `HttpContext.TraceIdentifier`) is stored
  in a separate, nullable `CorrelationId` column, so an audit row can be traced back to the log entries of the call that
  produced it.
- **RowVersion is sent in the request body** (`{ newStatus, rowVersion }`, Base64), not in an `If-Match` header.
  Reason: the bulk endpoint needs a RowVersion per item, which cannot be expressed with a single `If-Match` header; using
  the body in both endpoints keeps one consistent contract. Alternative considered: `ETag` / `If-Match` with
  `412 Precondition Failed` – more "HTTP-native" for single resources, documented as an option.

- **Solution layout:** production code under `src/` (`Tickets.Api`, `Tickets.Application`, `Tickets.Domain`,
  `Tickets.Infrastructure`), tests under `tests/Tickets.Tests`, Angular app under `client/`, documentation under `docs/`.
  Keeping tests outside `src/` is the common .NET convention and separates production code from test code.

## 4. Epics and tasks

Estimates are in hours of focused work.

### E0 – Planning, setup and cross-cutting foundation

| ID | Status | Task | Deliverable / acceptance | Est. | Depends on | Req. |
|---|---|---|---|---|---|---|
| T-01 | [x] | Analyze the specification, define requirement IDs, break into tasks | `docs/PLAN.md` | 2 | – | R9 |
| T-02 | [x] | Check installed tooling, fix technology versions | Versions table (this doc + README) | 0.5 | – | R11 |
| T-03 | [x] | Solution skeleton: 5 projects, references, `.gitignore`, `docker-compose.yml` (SQL Server + volume + healthcheck), connection string from configuration/env (no secrets in code), DI registration with documented lifetimes | Solution builds; `docker compose up` gives a healthy DB | 2 | T-01 | R10, R11 |
| T-04 | [x] | Cross-cutting: global `IExceptionHandler` → ProblemDetails (no stack trace outside Development), Serilog with CorrelationId in log scope, CORS for Angular dev, Swagger, `/health` | Unknown error returns ProblemDetails; logs carry CorrelationId (unhandled-exception path covered by an automated test in T-09) | 2 | T-03 | R10 |
| T-05 | [ ] | AI usage log, maintained throughout the work (what was generated, what was reviewed/changed manually) | `docs/AI_USAGE.md`, feeds the README section | 0.5 (ongoing) | – | R11 |

### E1 – Data model, seed and test infrastructure

| ID | Status | Task | Deliverable / acceptance | Est. | Depends on | Req. |
|---|---|---|---|---|---|---|
| T-06 | [x] | Design DB model (Ticket, TicketStatusHistory), column types, enum storage, and an index list per query scenario marked as **hypothesis** (to be confirmed or rejected by measurement in T-30/T-31) | `docs/DATA_MODEL.md` (summarized in README); index hypotheses in `docs/PERFORMANCE.md` | 1.5 | T-01 | R1, R6 |
| T-07 | [x] | Entities, enums, `DbContext`, Fluent configurations, `rowversion`, history with `RequestId` FK + `CorrelationId`; initial migration with PK/FK only (secondary indexes added after the baseline measurement) | Migration `InitialCreate`, applied on startup in Development | 2 | T-03, T-06 | R1, R2, R3 |
| T-08 | [x] | Seed command `seed --count N [--reset]`: `SqlBulkCopy` in batches, fixed Random seed, skewed distributions, ~50 orgs, ~20 assignees (some NULL), 2-year date range, some history rows, idempotent | 100k rows reproducibly; re-run without `--reset` does not duplicate | 3 | T-07 | DATA |
| T-09 | [x] | Test infrastructure: Testcontainers MsSql + `WebApplicationFactory`, known small dataset, DB reset between tests; isolated from the development database | `ApiFixture` with isolation guard; tests: isolated DB, health, unhandled exception → 500 ProblemDetails without internal details | 2 | T-07 | R8 |

### E2 – Read API (+ its tests)

| ID | Status | Task | Deliverable / acceptance | Est. | Depends on | Req. |
|---|---|---|---|---|---|---|
| T-10 | [x] | Query model + validator: page, pageSize ≤100, multi status/priority, org, assignee, date range, search, sortBy whitelist, sortDir | `TicketQuery`, `TicketQueryValidator` (unit tests), `FluentValidationFilter` → 400 `ValidationProblemDetails` (HTTP-level check in T-11/T-14) | 2 | T-07 | R1 |
| T-11 | [x] | `GET /api/tickets`: dynamic `IQueryable`, prefix search, sort with `Id` tie-breaker, `AsNoTracking` + projection to DTO, separate count, paged response | `PagedResult<TicketDto>`; no materialization before paging; `CancellationToken` passed down to EF | 3 | T-10 | R1 |
| T-12 | [x] | `GET /api/tickets/{id}` (RowVersion as Base64) and `GET /api/tickets/{id}/history` (newest first, limited/paged) | Two endpoints, DTOs, 404 for unknown id; `CancellationToken` | 1 | T-07 | R1, R3 |
| T-13 | [x] | `GET /api/tickets/summary`: count by status, by priority, top 10 assignees by open tickets – `GroupBy` translated to SQL (verified in SQL log) | `TicketSummaryDto`; `CancellationToken` passed down to EF | 1.5 | T-07 | R1 |
| T-14 | [x] | Integration tests for read: filter + sort + paging + totalCount on known data; validation (pageSize=1000, bad sortBy, reversed dates → 400, camelCase keys, `application/problem+json`); edge cases found manually in T-11: page beyond the end, `%`/`_` in search treated literally, `Id` tie-breaker (walking all pages with equal sort keys yields every row exactly once – no duplicates, no gaps); single ticket / history incl. 404 | Tests green | 2 | T-09, T-11 | R8, R1 |

### E3 – Write operations, concurrency, audit, cache (+ their tests)

| ID | Status | Task | Deliverable / acceptance | Est. | Depends on | Req. |
|---|---|---|---|---|---|---|
| T-15 | [x] | Status transition matrix as a single constant in the Application layer + unit tests | `StatusTransitions`, unit tests green | 1.5 | T-07 | R2, R8 |
| T-16 | [x] | `PUT /api/tickets/{id}/status` with body `{ newStatus, rowVersion }`: client RowVersion set as original value, `UpdatedAt` update, audit row in the same `SaveChanges`, 404 / 422 / 409 ProblemDetails, `ChangedBy` from `X-User-Name` | No lost update (enforced by DB); `CancellationToken`; logs for success and conflict (no sensitive data) | 3 | T-15 | R2, R3 |
| T-17 | [x] | Integration tests: (a) **deterministic** – two sequential updates with the same RowVersion → 200 then 409; (b) **parallel** – N simultaneous updates with the same RowVersion → exactly one 200, the rest 409; (c) **deterministic race** – another writer commits between our read and save (SaveChanges interceptor) → 409, other writer's change survives, no audit row; in all, exactly one audit row per success; invalid/same-status transition → 422; unknown id → 404; malformed body → 400 (regression for issue #18) | Tests green and stable across repeated runs | 2 | T-09, T-16 | R8, R2, R3 |
| T-18 | [x] | `POST /api/tickets/bulk-status`: ≤100 items, no duplicates, single read query, per-item save (partial success), detach on conflict, per-item outcome response | `BulkUpdateResponse`; `CancellationToken`; summary log (requested/succeeded/failed) | 3 | T-16 | R4 |
| T-19 | [x] | Integration test: bulk with valid + not found + invalid transition + conflict → correct per-item outcomes, valid items persisted; database-level conflict on one item does not affect the following items; 100-item boundary; invalid requests → 400 | Tests green (`TicketBulkUpdateTests`) | 1 | T-09, T-18 | R8, R4 |
| T-20 | [x] | Summary cache: `ISummaryCache` (Singleton), 60s absolute expiration, `SemaphoreSlim` against stampede; invalidation **after a successful commit** of single/bulk update (bulk: once, in `finally`); version check against stale write-back; test reset also clears the cache | `MemorySummaryCache`; `docs/CACHE.md` (why only the summary, expiration, invalidation, scale-out) | 2 | T-13, T-16, T-18 | R7 |
| T-21 | [x] | Integration test: summary → update → summary reflects the change (after first proving the cache is active); failed updates do not invalidate; bulk invalidates; unit tests for stampede and stale write-back | Tests green (`TicketSummaryCacheTests`, `MemorySummaryCacheTests`) | 0.5 | T-09, T-20 | R8, R7 |
| T-22 | [x] | `requests.http` with ready-to-run calls for every endpoint (incl. conflict and bulk scenarios) | `src/Tickets.Api/requests.http` | 0.5 | T-18 | R11 |

### E4 – Angular client

| ID | Status | Task | Deliverable / acceptance | Est. | Depends on | Req. |
|---|---|---|---|---|---|---|
| T-23 | [x] | Scaffold Angular app, dev proxy to API, models, `TicketsApiService` (HTTP only), interceptor (`X-User-Name`, error normalization) | App runs and talks to the API | 2 | T-11 | R5 |
| T-24 | [x] | `TicketsStore` + list page: filters component, search with `debounceTime` + duplicate suppression + `switchMap`, server-side sort/paging, filter change resets page, Loading / Empty / Error (Retry) | Working list page, all operations server-side. Duplicate suppression compares with the current query instead of `distinctUntilChanged` (which compares with the previous keystroke and would swallow a repeated search after "clear filters") | 5 | T-23 | R5 |
| T-25 | [x] | Summary panel, refreshed after updates | Component | 1 | T-24, T-13 | R5 |
| T-26 | [ ] | Single status update with RowVersion; 409 message + row refresh | Component | 2 | T-24, T-16 | R5 |
| T-27 | [ ] | Bulk: row selection (UI cap 100), status change, per-item results | Component | 2 | T-26, T-18 | R5 |
| T-28 | [ ] | History dialog | Component | 1 | T-24, T-12 | R5 |
| T-29 | [ ] | Frontend tests: debounce/switchMap cancellation, 409 handling | `ng test` green | 2 | T-24, T-26 | R5, R8 |

### E5 – Performance

| ID | Status | Task | Deliverable / acceptance | Est. | Depends on | Req. |
|---|---|---|---|---|---|---|
| T-30 | [ ] | Baseline on 100k rows without secondary indexes: EF-generated SQL for list + summary, `SET STATISTICS IO, TIME ON`, actual plans | `docs/sql/*.sql`, measured results | 2 | T-08, T-11, T-13 | R6 |
| T-31 | [ ] | Add justified indexes (migration), re-measure, confirm/reject each T-06 hypothesis, document rejected options, bottleneck + proposal, "no full load" explanation | `docs/PERFORMANCE.md`, `AddIndexes` migration | 3 | T-30 | R6 |

### E6 – Documentation and delivery

| ID | Status | Task | Deliverable / acceptance | Est. | Depends on | Req. |
|---|---|---|---|---|---|---|
| T-32 | [ ] | README: run steps, versions, structure, DB + indexes, concurrency + bulk, cache, 2 decisions, limitation + improvement, AI usage (from the log, to be completed by the author), assumptions, transition matrix; testing section stating explicitly that database-level concurrency enforcement is proven by the race (interceptor) test, while the parallel test verifies behavior only (from `docs/TESTING.md`) | `README.md` | 3 | all features, T-05 | R11 |
| T-33 | [ ] | Clean-run verification from scratch following the README: clean build (`dotnet build`, `ng build`, no warnings treated as ignorable), **all** tests (`dotnet test`, `ng test --watch=false`), docker → migrate → seed 100k → API → client, manual scenario check; final requirement checklist | All green, verified instructions, checklist | 2.5 | T-32 | R11, all |
| T-34 | [ ] | Git hygiene: meaningful commit history, `.gitignore`, push instructions | Repo ready to push | 0.5 | T-33 | Submission |

**Total estimate: 64.5 hours** (E0 7 · E1 8.5 · E2 9.5 · E3 13.5 · E4 15 · E5 5 · E6 6).

## 5. Execution order and critical path

```
T-01 → T-03 → T-04
          └→ T-07 → T-08 ───────────────────────────────────────┐
               ├→ T-09 (test infra)                             │
               ├→ T-10 → T-11 → T-14                            ├→ T-30 → T-31
               ├→ T-12                                          │
               ├→ T-13 ─────────────────────────────────────────┘
               └→ T-15 → T-16 → T-17
                           └→ T-18 → T-19
                                └→ T-20 → T-21 ;  T-22
T-11 → T-23 → T-24 → T-25 / T-26 → T-27 / T-28 → T-29
all features → T-32 → T-33 → T-34          (T-05 runs alongside everything)
```

Critical path: **T-01 → T-03 → T-07 → T-10 → T-11 → T-23 → T-24 → T-26 → T-27 → T-32 → T-33**.

Principles behind the order:
- Error handling and logging (T-04) are in place before the first endpoint, so every feature uses them from day one.
- Test infrastructure (T-09) comes right after the data model; each feature's tests are written immediately after it,
  so the backend is fully covered before the Angular work starts.
- Secondary indexes are deferred until after the baseline measurement (T-30), so the before/after comparison is real.

## 6. Traceability

| Req. | Tasks | Evidence (code / test / document) |
|---|---|---|
| R1 | T-06, T-10, T-11, T-12, T-13 | `TicketQueryService`, `TicketSummaryService`, `TicketQueryValidator`, `TicketsController`; `TicketQueryTests` (integration), `TicketQueryValidatorTests` (unit) |
| R2 | T-15, T-16 | `StatusTransitions`, `TicketCommandService`; `StatusTransitionsTests` (unit), `TicketStatusUpdateTests` (integration) |
| R3 | T-07, T-12, T-16 | `TicketStatusHistory`, history endpoint; audit assertions in `TicketStatusUpdateTests`, history in `TicketQueryTests` |
| R4 | T-18 | `BulkUpdateStatusAsync`, bulk endpoint; `TicketBulkUpdateTests` |
| R5 | T-23 – T-29 | `client/src/app/features/tickets/*`; frontend specs |
| R6 | T-06, T-30, T-31 | `docs/PERFORMANCE.md`, `docs/sql/`, index migration |
| R7 | T-20 | `MemorySummaryCache`, `docs/CACHE.md`; `TicketSummaryCacheTests`, `MemorySummaryCacheTests` |
| R8 | T-09, T-14, T-15, T-17, T-19, T-21, T-29 | `tests/Tickets.Tests`, client `*.spec.ts` |
| R9 | T-01 | this document |
| R10 | T-03, T-04 (and all) | project structure, `GlobalExceptionHandler`, `Program.cs` |
| R11 | T-02, T-05, T-22, T-32, T-33 | `README.md`, `docs/AI_USAGE.md`, `requests.http` |
| DATA | T-08 | Seed command, README run steps |

(Evidence column is updated with final file names as work completes.)

## 7. Risks

| Risk | Impact | Mitigation |
|---|---|---|
| Docker not running / unavailable | No DB, no integration tests | Docker Desktop required; LocalDB documented as fallback for local runs. InMemory/SQLite not used for concurrency tests. |
| `%term%` search causes full scans | Slow search on large data | Default to prefix search (index seek); Full-Text Search documented as the alternative. |
| `COUNT(*)` on wide filters dominates cost | Slow paging on broad queries | Measured; keyset pagination / skipping count documented as improvement. |
| Testcontainers slow on Windows | Slow test runs | One container per test run (shared fixture), data reset between tests. |
| Parallel concurrency test is timing-dependent | Flaky test | Assert the invariant (exactly one success, one audit row), not timing; keep a deterministic sequential variant as well. |
| Scope creep / over-engineering | Lower score, lost time | No CQRS/MediatR/repository-over-EF; thin controllers + services only. |
