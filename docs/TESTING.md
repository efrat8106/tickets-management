# Testing

## How to run

```bash
docker compose up -d        # not required for tests, but Docker must be running
dotnet test
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
