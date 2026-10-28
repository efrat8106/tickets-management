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
| 5 | Angular: server-side search/filter/sort/paging, debounce, cancel outdated requests, Loading/Empty/Error, update + 409 handling, aggregations, history, components vs services | `client/src/app` (`TicketsStore`, presentational components, `TicketsApiService`, interceptors) | `tickets.store.spec.ts`, `tickets-api.service.spec.ts` (18); real Chrome driven with Puppeteer on the 100k data (pre-submission check): two tabs → 409 warning + row refresh + successful retry, history dialog, debounce = exactly one network request for "Invoice", empty and error states, bulk results, sorting/paging as server requests – 17 / 17 checks | ✅ |
| 6 | ≥2 operations measured on 100k rows with plans, indexes explained, bottleneck + proposal, no full load before paging | `docs/PERFORMANCE.md`, `docs/sql/` | 9 scenarios, cold + 5 warm runs, actual plans; before → after; keyset proposal measured | ✅ |
| 7 | Cache for an aggregate with expiration, invalidation, justification, scale-out | `MemorySummaryCache`, `docs/CACHE.md` | `TicketSummaryCacheTests`, `MemorySummaryCacheTests`, mutation checks | ✅ |
| 8 | ≥4 meaningful tests incl. integration, valid scenario, validation, concurrency conflict | `tests/Tickets.Tests`, `client/**/*.spec.ts` | 96 tests, all green on the clean clone; mutation checks documented in `docs/TESTING.md` | ✅ |
| 9 | Work plan: breakdown, deliverables, order + dependencies, estimates, traceability, non-code tasks | `docs/PLAN.md` | 33 of 34 tasks done; T-34 (final Git hygiene / push) is left for the author. Traceability table points to code and tests | ✅ |
| 10 | Code quality: structure, naming, DTO mapping, DI lifetimes, async, global error handling, consistent validation, logging without sensitive data, no over-engineering | whole solution | Build with warnings-as-errors; one ProblemDetails format (500 path tested); validation through one filter; lifetimes documented in code and README | ✅ |
| 11 | Documentation: run instructions, versions, structure, DB + indexes, concurrency + bulk, cache, two decisions, limitation + improvement, AI usage | `README.md` + `docs/` | Run instructions executed literally on a clean clone (one fix needed: issue #24). The AI section is a draft to be completed by the author | ⚠️ AI section to be completed personally |

## Gap analysis against the specification

Every mandatory item (sections 1–11) is implemented. The points below are not missing requirements but places where a
reviewer could expect more, or where a deliberate decision limits the scope.

| # | Spec section | Point | Severity | Status / mitigation |
|---|---|---|---|---|
| G1 | 1 – text search | Search is **prefix** only (`LIKE 'term%'`), not "contains". | Low | Deliberate (index seek vs full scan), documented in README / PERFORMANCE with Full-Text Search as the alternative. |
| G2 | 1, 5 – filters | `organizationName` / `assignedTo` are exact matches; the UI has free-text inputs without suggestions. | Low (UX) | The prefix search covers partial organization names. Possible improvement: a lookup endpoint + autocomplete. |
| G3 | 5 – Angular | Angular 18 (as specified) is out of support; `npm audit` reports advisories in runtime packages. | Medium (security, not functionality) | Documented (issue #25); upgrade with `ng update` recommended before production. |
| G4 | 3 – audit `ChangedBy` | No authentication: `ChangedBy` comes from an optional, client-supplied header. | Low (out of scope) | Documented assumption; in production it would come from the authenticated user. |
| G5 | 1, 5 – date range | The UI sends whole **UTC** days; near midnight Israeli time a ticket can fall into the neighboring day. | Low | Documented; fix: send the user's local day boundaries converted to UTC. |
| G6 | 6 – paging | OFFSET paging gets slow on very deep pages (~308 ms at page 4,000). | Low at 100k | Documented as the known limitation; keyset alternative measured (0–3 ms). |
| G7 | 7 – cache | In-memory cache is per instance (up to 60 s stale with several instances). | Low (single instance here) | Documented scale-out path (Redis / Pub-Sub) behind `ISummaryCache`. |
| G8 | 10 – logging | Status-change log lines include the user name (`ChangedBy`). | Low | No secrets or ticket content are logged; the user name is an operational identifier. Could be removed/pseudonymized if policy requires. |
| G9 | 8 – tests | No browser end-to-end tests in the repository (the Puppeteer check was run before submission but not committed). | Low | Not required by the spec; unit + integration tests cover the logic. |
| G10 | 11 – docs | README "Use of AI tools" must be completed by the author; repository not yet pushed (T-34). | – | Author's action items. |

## Open points (honest)

| Point | Details | Suggested action |
|---|---|---|
| ⚠️ Angular 18 is out of support | `npm audit` (Oct 2026): 53 advisories, 8 in runtime packages (`@angular/core`, `@angular/common`, …, rated high); they are fixed only in newer Angular majors. Angular 18 was the version specified for the exercise. | Upgrade to a supported Angular major (`ng update`) before production use; for the exercise, documented as a known limitation (issue #25). |
| UI click-through | Done before submission with Puppeteer driving real Chrome (17 / 17 checks, see above). The automation script itself is not part of the repository. | Optional: one manual pass in the browser before the interview. |
| ⚠️ README "Use of AI tools" | Factual draft from `docs/AI_USAGE.md`. | Author completes and personalizes it. |
