# Testing

## How to run

```bash
# Backend (Docker must be running – the tests start their own SQL Server container)
dotnet test

# Frontend (headless Chrome)
cd client
npx ng test --watch=false --browsers=ChromeHeadless
```

Integration tests start their **own** SQL Server container with Testcontainers (random port, no volume, database
`TicketsTests`). They never touch the development database: the test host runs in the `Testing` environment (so
`appsettings.Development.json` is not loaded), and `ApiFixture` refuses to run if the API is connected to any other
database. Verified: the 100k development rows are unchanged after a full test run.

InMemory / SQLite providers are deliberately not used: they do not implement SQL Server `rowversion`, so concurrency
tests against them would prove nothing.

## Test suites

| Suite | Type | What it proves |
|---|---|---|
| `InfrastructureTests` | Integration | Tests run on the isolated container; `/health`; unhandled exception → 500 ProblemDetails without internal details, with correlation id |
| `TicketQueryValidatorTests` | Unit | Paging limits, enum values, lengths, date range order, `sortBy` whitelist, `sortDir` |
| `StatusTransitionsTests` | Unit | All 16 (from, to) status pairs, incl. same-status and forbidden transitions; guard test that the matrix test covers every pair |
| `TicketQueryTests` | Integration | Combined filters + sort + paging + totals; inclusive UTC date range; prefix search (incl. Hebrew); `%` `_` `[` treated literally; page beyond the end; `Id` tie-breaker (every row exactly once across pages); validation responses; details / history / 404; summary |
| `TicketStatusUpdateTests` | Integration | Valid update + audit row contents; 409 on stale RowVersion; parallel updates; **read/write race rejected by the database**; 422 transitions; 404; malformed bodies → 400 |
| `TicketSummaryCacheTests` | Integration | Each test first proves the cache is active (a row inserted directly into the DB is not visible), then: a committed single update invalidates it; failed updates (409, 422) do not; a bulk update with at least one success invalidates it |
| `MemorySummaryCacheTests` | Unit | 20 concurrent misses → one computation (stampede protection); invalidate; a computation that spans an invalidation is returned but not cached; a failed computation releases the lock |
| `TicketBulkUpdateTests` | Integration | Mixed request (updated / invalid transition / not found / conflict) → per-item outcomes in request order, only valid items persisted and audited; a database-level conflict on one item does not break the following items; exactly 100 items accepted; 101 items, duplicates, empty, bad item → 400 |

### Frontend (Jasmine / Karma)

| Suite | What it proves |
|---|---|
| `tickets.store.spec.ts` | **Debounce:** fast typing produces one request, sent 300 ms after the last keystroke, with page reset to 1; whitespace-only input sends nothing; the same text searched again after "clear filters" is not swallowed. **switchMap:** a query change unsubscribes (cancels) the in-flight request and a late response never overwrites newer data; previous data stays visible while loading. **Error state** + retry. **Status update:** last RowVersion sent; success replaces the row and reloads the summary; **409** replaces the row with `currentTicket` from the response (or reloads it if absent) and shows a warning, without reloading the summary; 422 shows the server message. **Bulk:** failed items stay selected with their fresh RowVersion, succeeded items are removed; selection capped at 100. |
| `tickets-api.service.spec.ts` | Query serialization (repeated `status`, omitted empty filters, trimmed text, UTC day boundaries); RowVersion in the update body; `X-User-Name` header only when set; 409 ProblemDetails → `ApiError` with `currentTicket`; 400 field errors → readable message |

The API mock returns a separate `Subject` per request, so tests control when each response arrives and can assert
that a cancelled request is no longer observed.

## Concurrency: which test proves what

| Test | Proves |
|---|---|
| `Write_between_our_read_and_our_save_is_rejected_by_the_database` | **Database-level enforcement (no lost update).** A `SaveChangesInterceptor` commits a change from another connection *after* the service has loaded the ticket and passed its early RowVersion check, right before `SaveChanges`. Only the `UPDATE … WHERE RowVersion = @original` check in SQL Server can detect this. The test asserts 409, that the other writer's change survives, and that no audit row is written. |
| `Parallel_updates_with_the_same_row_version_produce_exactly_one_success` | **Behavior only** (10 simultaneous requests → exactly one 200, nine 409, one audit row). It does *not* prove database enforcement on its own: in practice most requests are rejected by the early RowVersion check in the service. |
| `Second_update_with_the_same_row_version_gets_409_and_no_second_audit_row` | Deterministic sequential conflict and the shape of the 409 response (`currentTicket`). |

This was verified with a mutation test: with `rowversion` no longer configured as a concurrency token, **only the
race test failed** – the sequential and parallel tests still passed.

## Mutation checks performed

Tests were checked for real failure power by temporarily breaking the code (each change reverted and verified with
`git diff` afterwards):

| Mutation | Expected to fail | Result |
|---|---|---|
| Remove the `Id` tie-breaker from sorting | Paging tie-breaker test | `desc` variant failed (the `asc` variant alone would not detect it on small data, which is why both directions are tested) |
| `rowversion` not a concurrency token | Race test | Only the race test failed |
| Remove `Detach` of the failed entity after a concurrency exception | Bulk "database-level conflict on one item" test | Only that test failed (the next item's `SaveChanges` re-sent the failed change) |
| Remove cache invalidation after a single update | `Committed_status_update_invalidates_the_cached_summary` | Only that test failed |
| Remove the cache version check (always store) | `Summary_computed_across_an_invalidation_is_returned_but_not_cached` | Only that test failed |

| Frontend: list loading with `mergeMap` instead of `switchMap` | switchMap cancellation test | Only that test failed ("first HTTP request must be unsubscribed") |
| Frontend: debounce time 0 | Debounce test | Only that test failed |

Note: a mutation run only counts if the mutated code actually builds – with warnings-as-errors, a mutation that
introduces a warning silently leaves the old binaries in place (see issue #19).
