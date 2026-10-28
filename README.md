# Ticket Management System

A full-stack system for managing requests (tickets) received from organizations and employers. Multiple users can
search, filter, sort and page through 100,000+ tickets, change their status safely under concurrent use, update up to
100 tickets at once, view each ticket's change history and see aggregated statistics.

**Stack:** ASP.NET Core Web API (.NET 10) · EF Core · SQL Server 2022 · Angular 18

| Document | Contents |
|---|---|
| [docs/PLAN.md](docs/PLAN.md) | Work plan: epics, tasks, estimates, dependencies, critical path, traceability |
| [docs/DATA_MODEL.md](docs/DATA_MODEL.md) | Tables, types, enum storage, RowVersion, column lengths |
| [docs/PERFORMANCE.md](docs/PERFORMANCE.md) | Measurements before/after indexes, execution plans, bottlenecks |
| [docs/CACHE.md](docs/CACHE.md) | What is cached and why, expiration, invalidation, scale-out |
| [docs/TESTING.md](docs/TESTING.md) | Test suites, what each concurrency test proves, mutation checks |
| [docs/ISSUES_LOG.md](docs/ISSUES_LOG.md) | Problems found during development and how they were resolved |
| [docs/AI_USAGE.md](docs/AI_USAGE.md) | Detailed log of AI assistance per task |
| [docs/CHECKLIST.md](docs/CHECKLIST.md) | Requirement-by-requirement check after a clean run from scratch |
| [src/Tickets.Api/requests.http](src/Tickets.Api/requests.http) | Ready-to-run calls for every endpoint |

---

## 1. Running the solution

### Prerequisites

- **Docker Desktop** (running) – SQL Server, and the integration tests' own SQL Server container
- **.NET 10 SDK**
- **Node.js 20.19+** and npm
- Google Chrome (only for the headless frontend tests)

### Steps

Run from the repository root.

```bash
# 1. Database (SQL Server 2022 on localhost:14333). Wait until "healthy":
docker compose up -d
docker compose ps

# 2. Create the schema (EF migrations) and load 100,000 deterministic test tickets (~3 s).
#    Re-running without --reset does nothing; --reset recreates exactly the same data.
dotnet run --project src/Tickets.Api -- seed --count 100000

# 3. API on http://localhost:5010 (Swagger UI: http://localhost:5010/swagger, health: /health)
dotnet run --project src/Tickets.Api

# 4. Angular app on http://localhost:4200 (in a second terminal; /api is proxied to the API)
cd client
npm ci
npm start
```

### Tests

```bash
dotnet test                                                    # backend: unit + integration (Docker must be running)
cd client && npx ng test --watch=false --browsers=ChromeHeadless   # frontend
```

### Configuration

- The connection string is read from configuration (`ConnectionStrings:TicketsDb`). For local development it is in
  `src/Tickets.Api/appsettings.Development.json` and matches `docker-compose.yml`.
- The SA password in `docker-compose.yml` / `appsettings.Development.json` is a **throwaway development-only value**.
  Override it with the `MSSQL_SA_PASSWORD` environment variable (compose) and
  `ConnectionStrings__TicketsDb` (API). `appsettings.json` contains no connection string.
- In Development the API applies pending migrations on startup; the seed command always does.

---

## 2. Technologies and versions

| Area | Technology | Version |
|---|---|---|
| Backend | .NET SDK / ASP.NET Core Web API (Controllers) | 10.0.303 / 10.0 |
| ORM | EF Core + SQL Server provider | 10.0.14 |
| Database | SQL Server 2022 (Docker image `mssql/server:2022-latest`) | 16.0.4305.5 |
| Validation | FluentValidation | 12.2.0 |
| Logging | Serilog.AspNetCore (console) | 10.0.0 |
| API docs | Swashbuckle.AspNetCore | 10.3.1 |
| Frontend | Angular (standalone components, Signals, RxJS) | 18.2.14 |
| | RxJS / TypeScript | 7.8.2 / 5.5.4 |
| Backend tests | xUnit · FluentAssertions · Testcontainers.MsSql · Mvc.Testing | 2.9.3 · 7.2.0 · 4.16.0 · 10.0.14 |
| Frontend tests | Jasmine / Karma (headless Chrome) | 5.2 / 6.4 |
| Tooling | Node.js / npm / Docker | 20.19.4 / 10.8.2 / 29.6.1 |

.NET 10 was chosen over .NET 8: it is the current LTS, .NET 8 support ends in November 2026, and only .NET 10 was
installed on the development machine. FluentAssertions is pinned to 7.x (Apache 2.0) because version 8 is commercially
licensed.

---

## 3. Solution structure

```
src/
  Tickets.Domain/          Entities (Ticket, TicketStatusHistory) and enums – no dependencies
  Tickets.Application/     Services (query, summary, commands), validators, DTOs, status transitions,
                           abstractions (ITicketsDbContext, ISummaryCache)
  Tickets.Infrastructure/  EF DbContext + configurations + migrations, seed command, memory cache
  Tickets.Api/             Controllers, global error handling, correlation id, validation filter, Program.cs
tests/
  Tickets.Tests/           Unit tests and integration tests (WebApplicationFactory + Testcontainers SQL Server)
client/                    Angular app
  src/app/core/            API service (HTTP only), interceptors (errors, X-User-Name), models, user name
  src/app/features/tickets TicketsStore (screen state) + presentational components:
                           list, filters, summary, status select, bulk bar, history dialog
docs/                      Plan, data model, performance (+ docs/sql: queries, harness, raw results, plans), cache, tests
docker-compose.yml         SQL Server for local development
```

Dependencies point inwards: Api → Application ← Infrastructure, Application → Domain. Controllers are thin; business
logic is in Application services, which use EF Core `IQueryable` directly through `ITicketsDbContext` (no repository
layer on top of EF, no CQRS/MediatR – deliberately simple).

**DI lifetimes:** `TicketsDbContext` / `ITicketsDbContext`, the services and validators are **Scoped** (one unit of work
per request; a DbContext is not thread-safe). `ISummaryCache` and `TimeProvider` are **Singletons** (one cache and one
fill lock per process; stateless clock).

### API

| Method | Route | Description | Responses |
|---|---|---|---|
| GET | `/health` | Database connectivity | 200 / 503 |
| GET | `/api/tickets` | Paging, combined filters (`status`, `priority` – repeatable; `organizationName`, `assignedTo`, `createdFrom`, `createdTo`), prefix `search`, `sortBy` (`createdAt`, `updatedAt`, `priority`, `status`, `title`, `organizationName`) + `sortDir`; `pageSize` ≤ 100 | 200 / 400 |
| GET | `/api/tickets/summary` | Counts by status, by priority, top 10 assignees by open tickets (cached) | 200 |
| GET | `/api/tickets/{id}` | One ticket incl. RowVersion | 200 / 404 |
| GET | `/api/tickets/{id}/history` | Status history, newest first, paged | 200 / 400 / 404 |
| PUT | `/api/tickets/{id}/status` | `{ newStatus, rowVersion }`, optional `X-User-Name` header | 200 / 400 / 404 / 409 / 422 |
| POST | `/api/tickets/bulk-status` | `{ newStatus, items: [{ id, rowVersion }] }` (≤ 100) – partial success | 200 / 400 |

All errors are RFC 7807 `ProblemDetails` (`application/problem+json`) with a `correlationId`; validation errors use
`ValidationProblemDetails` with camelCase field keys. Unexpected exceptions return 500 without internal details outside
Development.

---

## 4. Database and indexes

**SQL Server** was chosen (see decision 1 below). Key model decisions ([details](docs/DATA_MODEL.md)):
`int IDENTITY` keys (narrow, ever-increasing), enums stored as `tinyint` with explicit values and CHECK constraints
(1 byte, and numeric order = business order, so sorting by priority is correct), bounded `nvarchar` lengths (indexable;
Hebrew text), `datetime2(3)` in UTC, and a SQL Server `rowversion` column for concurrency.

**Indexes** – formulated as hypotheses, then each one confirmed, changed or rejected by measurement on 100k rows
([PERFORMANCE.md](docs/PERFORMANCE.md)):

| Index | Supports |
|---|---|
| `IX_Tickets_CreatedAt` | Default sort (newest first) without sorting, date ranges |
| `IX_Tickets_Status_Priority_CreatedAt` | Status / priority filters; GROUP BY status / priority |
| `IX_Tickets_AssignedTo_CreatedAt` INCLUDE (Status) | Assignee + date range; top assignees by open tickets |
| `IX_Tickets_Title`, `IX_Tickets_OrganizationName` | Prefix search (`LIKE 'term%'` = index seek) on either column; organization filter |
| `IX_TicketStatusHistory_RequestId` | History of a ticket |

Measured effect (logical reads, warm median elapsed): default list 4,046 → 282 reads (53 → 12 ms), status filter
3,946 → 694 (44 → 6 ms), prefix search 3,946 → 862 (71 → 7 ms), summary 5,919 → 988 (118 → 63 ms). Rejected after
measuring: a `(RequestId, ChangedAt DESC)` history index (no gain), an `UpdatedAt` index (changes on every write),
covering indexes (would duplicate the table).

**No full loading into memory:** every filter, sort and page is part of the SQL query (`IQueryable` → `WHERE … ORDER BY
… OFFSET … FETCH`), results are projected straight into DTOs with `AsNoTracking`, the total is a separate `COUNT(*)`,
and aggregations are `GROUP BY` in SQL. The Angular client never filters, sorts or pages locally.

---

## 5. Concurrency, status changes and bulk update

### Status transitions

Defined once (`StatusTransitions`); everything else – including a change to the same status – returns **422** with the
allowed statuses.

| From \ To | New | InProgress | Waiting | Completed |
|---|---|---|---|---|
| **New** | – | ✅ | – | – |
| **InProgress** | – | – | ✅ | ✅ |
| **Waiting** | – | ✅ | – | ✅ |
| **Completed** | – | ✅ (reopen) | – | – |

### Optimistic concurrency (enforced by the database)

1. Every ticket returned by the API carries its `rowVersion` (Base64). SQL Server increments it on every row update.
2. The client sends back the `rowVersion` it saw: `PUT /api/tickets/{id}/status { newStatus, rowVersion }`. The
   RowVersion is sent in the **body** (not `If-Match`) so the single and bulk endpoints share one contract.
3. The server loads the ticket (404 if missing), returns **409** right away if the client's version is already outdated,
   validates the transition (422), and sets the client's value as EF's *original* RowVersion.
4. EF sends `UPDATE … WHERE Id = @id AND RowVersion = @clientRowVersion`. If anyone changed the row in between, 0 rows
   are affected → `DbUpdateConcurrencyException` → **409** with the ticket's current state (`currentTicket`).
5. The status change, `UpdatedAt` and the **audit row** (`TicketStatusHistory`: RequestId, PreviousStatus, NewStatus,
   ChangedAt, ChangedBy from `X-User-Name` or "system", CorrelationId) are saved in **one `SaveChanges` = one
   transaction**: a failed update leaves no audit row. Success returns 200 with the new RowVersion.

Because the check and the write are one atomic SQL statement, there is no lost update – the UI only displays the 409;
it does not enforce anything. In Angular, a 409 replaces the row with `currentTicket` and tells the user the ticket was
changed by someone else, so they decide again on fresh data.

### Bulk update – partial success

`POST /api/tickets/bulk-status` accepts up to 100 items (no duplicates; otherwise 400). All tickets are loaded with one
`WHERE Id IN (…)` query; then **each item goes through exactly the same logic as a single update**, in its own
transaction with its own audit row. The response (200) reports every item in request order:
`{ requested, succeeded, failed, results: [{ id, outcome: Updated | NotFound | InvalidTransition | Conflict, ticket, message }] }`
– `ticket` is the updated ticket or its current state, so the client can refresh and retry failed items.

**Why partial success over all-or-nothing:** with many concurrent users it is likely that one of 100 tickets changed in
the meantime; rolling back the other 99 would make bulk updates fail repeatedly. The items are independent business-wise,
and the per-item result tells the client exactly what to retry. Trade-off: no atomicity across the request – if it is
cancelled midway, items already saved stay saved (and the cache is still invalidated).

---

## 6. Cache and invalidation

Only the **summary** is cached ([details](docs/CACHE.md)): it is relatively expensive (three aggregations over the whole
table), read by every user on the main screen, identical for everyone and tolerant of a few seconds' staleness. The
ticket list is not cached – its key space (filters × sort × page) is huge and rows must be fresh for RowVersion-based
updates.

- `IMemoryCache` behind `ISummaryCache`, absolute expiration **60 s** (safety net).
- **Invalidation after a committed change only:** after a successful single update, and once at the end of a bulk
  request if at least one item succeeded (in a `finally`, so also when the request is cancelled midway). Failed updates
  (404 / 409 / 422) keep the cache.
- Stampede protection: one request computes on a miss (`SemaphoreSlim`), others wait.
- A version counter prevents a computation that started before an update from writing stale data back after the
  invalidation.

**Scale-out:** an in-memory cache is per instance – an update on instance A does not invalidate instance B, which can
serve data up to 60 s old. With several instances, `ISummaryCache` would be implemented with Redis (shared key, shared
invalidation) or keep local caches and broadcast invalidations via Pub/Sub. Callers do not change.

---

## 7. Two significant technology decisions

### Decision 1 – SQL Server with EF Core (and `rowversion`)

- **Why:** the data is relational and uniform; the requirements are server-side filtering, sorting, paging,
  aggregations and strict optimistic concurrency. SQL Server gives a native `rowversion` that EF Core uses as a
  concurrency token, B-tree indexes for prefix search and range filters, and `GROUP BY` for aggregations. EF Core
  translates the composed `IQueryable` into exactly the SQL we want (verified in the log) while keeping the code
  type-safe.
- **Alternatives considered:**
  - *MongoDB* – flexible schema is not needed here; optimistic concurrency would need a manual version field and
    conditional updates; aggregations via pipelines.
  - *Dapper* (for some or all queries) – more control, but hand-written dynamic SQL for the combinable filters and
    sorting, and manual concurrency handling. EF's output was already efficient, so it was not needed.
  - *SQLite / EF InMemory for tests* – rejected: no real `rowversion`; integration tests run on SQL Server via
    Testcontainers instead.

### Decision 2 – Optimistic concurrency with RowVersion (not locking), partial-success bulk

- **Why:** users look at a ticket for a while before changing it. Optimistic concurrency holds no locks during that time,
  scales with many users, and the database still guarantees no lost update (`UPDATE … WHERE RowVersion = @original`).
  Conflicts are expected to be rare and are surfaced as 409 with the current state.
- **Alternatives considered:**
  - *Pessimistic locking* (`UPDLOCK`/holding a transaction or a "checked out" flag) – blocks other users, needs lock
    timeouts and cleanup.
  - *Last write wins* – simple but loses updates, which the requirements forbid.
  - *`ETag` / `If-Match` with 412* – more HTTP-idiomatic for single resources, but a bulk request needs one version per
    item, so the version is in the body for both endpoints.
  - *All-or-nothing bulk* – see section 5.

Smaller decisions: Controllers (not Minimal APIs) for a conventional structure with filters and response metadata;
in Angular a per-screen store with Signals + RxJS instead of NgRx (no global state needed); FluentValidation for one
consistent validation style; 422 for business-rule violations vs 400 for malformed input.

---

## 8. Known limitation and a possible improvement

**Known limitation – deep pages with OFFSET paging.** Page 4,000 of the default list (`OFFSET 79980`) reads 2,073 pages
and takes ~308 ms, unchanged by indexing, because SQL Server must produce and discard all preceding rows. Shallow pages
are fast (12 ms). (Also: in a multi-instance deployment the in-memory summary cache can be up to 60 s stale.)

**Improvement – keyset pagination.** Send the `CreatedAt` + `Id` of the last row instead of a page number and query
`WHERE CreatedAt < @last OR (CreatedAt = @last AND Id < @lastId)`. Measured on the same deep page: **65–72 logical reads
and 0–3 ms** instead of 2,073 reads / ~308 ms, with identical rows
([experiment](docs/sql/experiments/keyset-deep-page.sql)). The trade-off is losing "jump to page N", so it would be
offered as "load more" / next-previous.

Other improvements listed in [PERFORMANCE.md](docs/PERFORMANCE.md): avoiding a total count on every request,
Full-Text Search for "contains" search, a Redis-backed summary cache.

**Also noted:** Angular 18 (the version specified for this exercise) is out of support; `npm audit` reports advisories
in Angular runtime packages that are fixed only in newer majors. **Decision:** kept as a documented limitation – no
upgrade before submission. Before production use the client should be upgraded to a supported Angular version with
`ng update`.

A requirement-by-requirement check after a clean run from scratch is in [docs/CHECKLIST.md](docs/CHECKLIST.md).

---

## 9. Testing

96 automated tests – 78 backend (xUnit: unit + integration) and 18 frontend (Jasmine) – all green. Details in
[docs/TESTING.md](docs/TESTING.md).

- **Integration tests** run the real API (`WebApplicationFactory`) against a **dedicated SQL Server container**
  (Testcontainers) – never the development database (guarded by a check in the fixture).
- Covered: filtering / sorting / paging / totals and edge cases (page beyond the end, `%` and `_` in search, tie-breaker
  across pages), validation (400 with camelCase keys), 404 / 422, status update + audit, bulk mixed outcomes, cache
  invalidation, stampede protection, the transition matrix (all 16 pairs), and in Angular: debounce, `switchMap`
  cancellation, 409 handling, bulk selection.
- **Concurrency – what proves what:**
  - **Database-level enforcement is proven by the race test** (`Write_between_our_read_and_our_save_is_rejected_by_the_database`):
    a `SaveChangesInterceptor` commits a change from another connection after the service has read the ticket and
    passed its early check, right before `SaveChanges`. Only the `rowversion` check inside the `UPDATE` can detect it;
    the test asserts 409, that the other writer's change survives, and that no audit row is written.
  - **The parallel test verifies behavior only** (10 simultaneous requests → exactly one 200, nine 409, one audit row).
    On its own it does not prove database enforcement – a mutation test showed it still passes when `rowversion` is not
    a concurrency token, because most requests are stopped by the early check.
- Mutation checks were used to confirm that key tests fail when the protected behavior is removed (tie-breaker, concurrency
  token, detach in bulk, cache invalidation, cache version check, `switchMap`, debounce).

---

## 10. Assumptions and decisions

- **"RequestId"** in the audit is the ticket id (the specification calls a ticket a "request"). The HTTP correlation id
  is stored separately in `CorrelationId` (from `X-Correlation-Id` or generated), linking audit rows to log entries.
- **No authentication** (not in scope). `ChangedBy` comes from the optional `X-User-Name` header (max 100 characters;
  default `"system"`); the Angular app lets the user type a name, stored in the browser.
- **Search** is a case-insensitive **prefix** search on Title or OrganizationName (index-friendly); `organizationName`
  and `assignedTo` filters are exact matches.
- **Dates** are stored and compared in UTC. `createdFrom` / `createdTo` are inclusive; the Angular date pickers send
  whole UTC days.
- **Bulk** uses one target status for all items; up to 100 items; duplicates are rejected.
- **History** is returned newest first, paged (`pageSize` ≤ 100).
- **Seed data** is deterministic (fixed random seed and a fixed end date of 2026-10-01): ~50 organizations (Hebrew and
  English), 20 assignees, skewed distributions (most tickets completed, a few large organizations, some unassigned).
- **Enum values** in the API are names (`"InProgress"`), stored as numbers in the database.

---

## 11. Use of AI tools

Based on [docs/AI_USAGE.md](docs/AI_USAGE.md), a per-task log kept during the work.

### Tools

- **Claude Code** (AI coding agent in VS Code) – generated the code, tests, scripts and documentation, ran builds,
  tests, measurements and manual checks, and kept the logs.
- **Claude (chat)** – used by the author as a second reviewer of the plan and of intermediate results.

### How the work was done

The author defined the requirements, the technology stack and the order of work (a detailed prompt based on the
specification), then worked task by task: after each task the agent reported what it did and why, the author reviewed
it and approved or directed changes, and every task ended with a local commit. AI was used for **all parts** of the
solution: work plan, project skeleton, data model, seed command, backend (queries, status update and concurrency,
bulk, cache, error handling, validation, logging), Angular client, automated tests, performance measurement and
documentation.

### Decisions and corrections directed by the author (examples)

- **Plan (T-01):** restructured after review – `RequestId` is the ticket FK and the HTTP correlation id is stored
  separately; error handling before the first endpoint; each feature's tests right after it; corrected hour totals;
  CancellationToken / logging in the acceptance criteria; RowVersion sent in the body; both a deterministic and a
  parallel concurrency test; index list labelled as hypotheses until measured; a running AI log and issues log.
- **Testing:** integration tests on a separate Testcontainers database that can never reset the development data;
  edge cases found manually (page beyond the end, `%` in search, tie-breaker paging) turned into tests; the docs state
  that database-level concurrency is proven by the race test and that the parallel test verifies behavior only.
- **Cache:** invalidate only after a successful commit, clear the cache on test reset, document why only the summary is
  cached and how to scale out.
- **Performance:** reseed before measuring, stop the app, measure cold and warm, keep scripts and raw results, report
  real numbers only, reject indexes that do not help.
- **Delivery:** local commits only (no remote / push by the agent); a clean run from scratch; a final pre-submission
  check (tests, scenarios through API and Angular proxy, two browser tabs, Git hygiene, gap analysis).
- **Technology:** kept Angular 18 as specified; the out-of-support finding was raised to the author instead of the
  agent upgrading on its own.

### How the AI's output was verified

- **By running it, not only by reading it:** HTTP calls against the 100k data set, the SQL generated by EF (log),
  the schema in SQL Server, the UI in a real browser (Puppeteer, two tabs), and all of `requests.http`.
- **96 automated tests** (78 backend, 18 frontend), all green on a fresh clone.
- **Mutation tests:** key behaviors (tie-breaker, concurrency token, detach in bulk, cache invalidation, cache version
  check, `switchMap`, debounce) were removed on purpose to confirm the corresponding test fails.
- **Measurements:** every performance number comes from `docs/sql/results`; the measurement script's own bug was caught
  and the baseline re-run.

### What the verification found in the AI-generated work

21 bugs were found and fixed (all listed in [ISSUES_LOG.md](docs/ISSUES_LOG.md)), for example:
- a `500` for the request body `{}` (validation rule running on a null value);
- a stale RowVersion kept for failed items after a bulk update in the UI (found by a unit test);
- a LINQ query EF could not translate (summary `500`);
- validation messages in Hebrew from the server's OS culture, PascalCase error keys, an internal type name leaking in a
  `400` response;
- `npm ci` failing on a fresh clone because of an out-of-sync lock file;
- a file corrupted by the agent's own PowerShell edit, and false results in its own verification scripts (mutation run
  on stale binaries, 0 ms timings, browser-automation timing) – caught because results were questioned, not accepted.
- One incorrect technical claim by the AI (that `status=7` passes model binding) was disproved by testing and corrected.

### Author's own part

- I reviewed the work plan (`docs/PLAN.md`) with the help of a second AI assistant (Claude chat). I examined its
  comments and decided on them: the meaning of `RequestId` in the history table (FK to the ticket, with a separate
  `CorrelationId`), moving error handling before the endpoints, writing tests right after each feature, correcting the
  total hours, adding CancellationToken and logging to the acceptance criteria, invalidating the cache only after a
  commit, sending the RowVersion in the body, and testing concurrency both deterministically and in parallel.
- I approved each task before moving to the next one, and I stopped the work at key points (after the status update
  and concurrency tests, and after the bulk update and the cache) to get an explanation and make sure I understand the
  solution.
- I decided that work would be committed locally only, and that I decide myself when to upload to GitHub.
- I ran the system in the browser and noticed one compilation error (`mergeMap`) that came from a mutation test running
  at the same time.
- I decided to keep Angular 18 as a documented limitation and not to upgrade before submission.
- I checked which GitHub account I am signed in to, so the code is uploaded to my personal account and not to another
  organization.

**Parts of this work, including code and tests, were generated by Claude Code and are documented in
[docs/AI_USAGE.md](docs/AI_USAGE.md). I am responsible for all submitted code.**

---

## Publishing the repository

The repository has a local Git history only (no remote configured). To publish it:

```bash
# create an empty repository on GitHub / GitLab first (without README / .gitignore), then:
git remote add origin https://github.com/<user>/<repository>.git
git push -u origin main
```
