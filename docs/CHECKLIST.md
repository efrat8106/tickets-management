# Requirement Checklist

Final check of every requirement in the specification against the implementation, after a clean run from scratch
(fresh `git clone`, database container and volume deleted, README steps followed literally).

✅ implemented and verified · ⚠️ implemented with a stated limitation

## Clean run (T-33)

| Step | Result |
|---|---|
| `git clone` into an empty folder (no `bin`/`obj`/`node_modules`) | 277 files |
| `docker compose up -d` (new volume) | healthy after ~30 s |
| `dotnet run --project src/Tickets.Api -- seed --count 100000` | both migrations applied, 5 indexes, 100,000 tickets + 17,151 history rows (seed 4.7 s; 31 s incl. restore/build) |
| `dotnet run --project src/Tickets.Api` | `/health` 200, Swagger UI 200 |
| `npm ci` | **failed** at first (lock file out of sync) → lock file regenerated and committed → passes (issue #24) |
| `npm start` | UI rendered with real data (headless Chrome), `/api` proxied |
| `dotnet build` | 0 warnings, 0 errors (warnings-as-errors) |
| `dotnet test` | 78 / 78 passed |
| `ng build` | succeeded, no warnings |
| `ng test --watch=false` | 18 / 18 passed |
| All calls in `requests.http` | every response has the documented status (200/400/404/409/422, bulk 1 updated + 2 failed) |
| Two "tabs" through the Angular proxy | tab A 200, tab B (old RowVersion) 409 with `currentTicket` = tab A's status; history shows only tab A's change |

## Specification

| # | Requirement | Where | How verified | Status |
|---|---|---|---|---|
| 1 | Server-side pagination, combined filters (status, priority, organization, assignee, date range), text search on Title + OrganizationName, sorting (6 fields, asc/desc), ≥2 aggregations, page size limit + validation, CancellationToken, DTOs, no full load | `TicketQueryService`, `TicketSummaryService`, `TicketQueryValidator`, `TicketsController`, DTOs | `TicketQueryTests` (15), `TicketQueryValidatorTests` (14), EF SQL log, `docs/PERFORMANCE.md` "How loading everything is avoided" | ✅ |
| 2 | 404 for missing ticket, clear validation for invalid status / transition, optimistic concurrency (one success, others 409), UpdatedAt + RowVersion consistent, no lost update | `TicketCommandService`, `StatusTransitions`, `rowversion` | `TicketStatusUpdateTests` incl. the deterministic read/write race (database-level proof) and parallel test; `StatusTransitionsTests` (all 16 pairs) | ✅ |
| 3 | Audit per status change (RequestId, PreviousStatus, NewStatus, ChangedAt, ChangedBy) + history endpoint | `TicketStatusHistory`, `GET /api/tickets/{id}/history` | Audit row contents asserted; one audit row per success, none on failure; history order/paging/404 tested | ✅ |
| 4 | Bulk update ≤100, defined behavior for missing / conflicting items, justified choice, clear response | `BulkUpdateStatusAsync`, `POST /api/tickets/bulk-status`; README §5 | `TicketBulkUpdateTests` (mixed outcomes, DB-level conflict mid-bulk, 100-item boundary, invalid requests) | ✅ |
| 5 | Angular: server-side search/filter/sort/paging, debounce, cancel outdated requests, Loading/Empty/Error, update + 409 handling, aggregations, history, components vs services | `client/src/app` (`TicketsStore`, presentational components, `TicketsApiService`, interceptors) | `tickets.store.spec.ts`, `tickets-api.service.spec.ts` (18), headless render on 100k data, two-tab check via proxy. Mouse interaction could not be automated in this environment – to be clicked through once by the author | ✅ |
| 6 | ≥2 operations measured on 100k rows with plans, indexes explained, bottleneck + proposal, no full load before paging | `docs/PERFORMANCE.md`, `docs/sql/` | 9 scenarios, cold + 5 warm runs, actual plans; before → after; keyset proposal measured | ✅ |
| 7 | Cache for an aggregate with expiration, invalidation, justification, scale-out | `MemorySummaryCache`, `docs/CACHE.md` | `TicketSummaryCacheTests`, `MemorySummaryCacheTests`, mutation checks | ✅ |
| 8 | ≥4 meaningful tests incl. integration, valid scenario, validation, concurrency conflict | `tests/Tickets.Tests`, `client/**/*.spec.ts` | 96 tests, all green on the clean clone; mutation checks documented in `docs/TESTING.md` | ✅ |
| 9 | Work plan: breakdown, deliverables, order + dependencies, estimates, traceability, non-code tasks | `docs/PLAN.md` | 33 of 34 tasks done; T-34 (final Git hygiene / push) is left for the author. Traceability table points to code and tests | ✅ |
| 10 | Code quality: structure, naming, DTO mapping, DI lifetimes, async, global error handling, consistent validation, logging without sensitive data, no over-engineering | whole solution | Build with warnings-as-errors; one ProblemDetails format (500 path tested); validation through one filter; lifetimes documented in code and README | ✅ |
| 11 | Documentation: run instructions, versions, structure, DB + indexes, concurrency + bulk, cache, two decisions, limitation + improvement, AI usage | `README.md` + `docs/` | Run instructions executed literally on a clean clone (one fix needed: issue #24). The AI section is a draft to be completed by the author | ⚠️ AI section to be completed personally |

## Open points (honest)

| Point | Details | Suggested action |
|---|---|---|
| ⚠️ Angular 18 is out of support | `npm audit` (Oct 2026): 53 advisories, 8 in runtime packages (`@angular/core`, `@angular/common`, …, rated high); they are fixed only in newer Angular majors. Angular 18 was the version specified for the exercise. | Upgrade to a supported Angular major (`ng update`) before production use; for the exercise, documented as a known limitation (issue #25). |
| ⚠️ UI click-through | Logic is covered by unit tests and the API flows by integration tests and HTTP checks, but no browser automation of clicks was available here. | Open `http://localhost:4200`, try filters / sort / search / paging / update / bulk / history, and two tabs for a 409. |
| ⚠️ README "Use of AI tools" | Factual draft from `docs/AI_USAGE.md`. | Author completes and personalizes it. |
