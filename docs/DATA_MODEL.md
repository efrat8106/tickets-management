# Data Model

SQL Server 2022, mapped with EF Core 10 (Fluent configuration). All timestamps are UTC.

## Tables

### `Tickets`

| Column | SQL type | Null | Notes |
|---|---|---|---|
| `Id` | `int IDENTITY` | no | Primary key, clustered |
| `Title` | `nvarchar(200)` | no | |
| `OrganizationName` | `nvarchar(200)` | no | |
| `Status` | `tinyint` | no | `CHECK (Status BETWEEN 1 AND 4)` |
| `Priority` | `tinyint` | no | `CHECK (Priority BETWEEN 1 AND 3)` |
| `AssignedTo` | `nvarchar(100)` | yes | `NULL` = unassigned |
| `CreatedAt` | `datetime2(3)` | no | Set once on creation |
| `UpdatedAt` | `datetime2(3)` | no | Set by the application on every change |
| `RowVersion` | `rowversion` | no | Concurrency token, maintained by SQL Server |

### `TicketStatusHistory`

| Column | SQL type | Null | Notes |
|---|---|---|---|
| `Id` | `bigint IDENTITY` | no | Primary key, clustered |
| `RequestId` | `int` | no | FK → `Tickets.Id` (the ticket = "request" in the specification), `ON DELETE NO ACTION` |
| `PreviousStatus` | `tinyint` | no | Same CHECK as `Tickets.Status` |
| `NewStatus` | `tinyint` | no | Same CHECK as `Tickets.Status` |
| `ChangedAt` | `datetime2(3)` | no | |
| `ChangedBy` | `nvarchar(100)` | no | From `X-User-Name`, default `system` |
| `CorrelationId` | `varchar(64)` | yes | HTTP correlation id of the call that made the change |

## Decisions

### Id type – `int IDENTITY` (Tickets), `bigint IDENTITY` (history)

- **Narrow:** 4 bytes. The clustered key is copied into every non-clustered index row, so a narrow key keeps
  all indexes smaller and more pages fit in memory.
- **Ever-increasing:** new rows go to the end of the clustered index – no page splits or fragmentation on insert.
- **Capacity:** `int` allows ~2.1 billion tickets, far above the expected volume.
- **History uses `bigint`:** it is append-only and grows several times faster than tickets (one row per status
  change); 4 extra bytes per row buy unlimited headroom on a table that is rarely indexed by anything other than
  `RequestId`.
- **Alternatives considered:** `uniqueidentifier` (random GUID) – 16 bytes and random insert order, causing
  fragmentation; `NEWSEQUENTIALID()` fixes ordering but still 4× wider. The benefit of GUIDs (generating ids
  outside the DB, merging data from several sources) is not needed here. Sequential ids are guessable, which is
  acceptable because ids are not used as an authorization mechanism.

### Enum storage – `tinyint` with explicit values

| Status | Value | | Priority | Value |
|---|---|---|---|---|
| New | 1 | | Low | 1 |
| InProgress | 2 | | Medium | 2 |
| Waiting | 3 | | High | 3 |
| Completed | 4 | | | |

- **Size:** 1 byte versus 8–20 bytes for `nvarchar` – this matters because `Status` and `Priority` are leading
  columns in the main filter indexes.
- **Correct sort order:** sorting by `Priority` must mean Low → Medium → High. As strings, it would sort
  alphabetically (High, Low, Medium) – wrong. Numeric values follow the business order (and the workflow order for
  `Status`), so `ORDER BY Priority` is correct and index-supported.
- **Explicit values** in the C# enum (`New = 1`, …) so reordering the enum in code can never silently change the
  meaning of stored data. `0` is deliberately unused, so an uninitialized value is invalid.
- **Integrity in the database:** `CHECK` constraints reject out-of-range values even from writes that bypass the
  application (manual SQL, scripts).
- **API contract uses names**, not numbers (`"status": "InProgress"`), via `JsonStringEnumConverter` – readable for
  clients and decoupled from storage.
- **Trade-off:** raw SQL shows numbers. Mitigated by the mapping table above.
  Alternative considered: `varchar` columns (self-describing, but wider and wrong sort order) or a lookup table with
  FK (over-engineering for fixed, code-defined values).

### RowVersion – SQL Server `rowversion`

- 8-byte value that **SQL Server itself** increments on every update of the row – the application cannot forget to
  bump it, and two writers can never produce the same value.
- Configured with `IsRowVersion()`, which makes it an EF concurrency token: an update is sent as
  `UPDATE … WHERE Id = @id AND RowVersion = @original`. If another writer changed the row in between, 0 rows are
  affected and EF throws `DbUpdateConcurrencyException`, which the API maps to `409 Conflict`. The check and the
  write are **one atomic statement in the database** – no read-check-write race, no lost update.
- The client receives it as Base64 and sends back the value it saw; the server sets it as the *original* value
  before saving, so the comparison is against what the user actually saw, not what the server just loaded.
- `UpdatedAt` is set in the same `UPDATE`, so `UpdatedAt` and `RowVersion` always change together.
- **Alternative considered:** an `int Version` column incremented by the application – portable to other databases,
  but relies on every write path remembering to increment it.

### Column lengths and types

- **`nvarchar`** for all text: organization names, titles and assignee names may be in Hebrew.
- **Bounded lengths instead of `nvarchar(max)`:** `max` columns cannot be index keys, and the search/filter
  scenarios require indexes on `Title`, `OrganizationName` and `AssignedTo`.
  - `Title` 200 – a short subject line, not a description.
  - `OrganizationName` 200 – covers long legal company names.
  - `AssignedTo` / `ChangedBy` 100 – a user name or display name.
  - 200 `nvarchar` characters = 400 bytes, well under the 1,700-byte non-clustered index key limit, even in composite keys.
- **The same limits are enforced by validation in the API**, so too-long input gets a clear 400 rather than a
  database truncation error.
- **`CorrelationId` is `varchar(64)`:** the middleware only accepts ASCII ids up to 64 characters.
- **`datetime2(3)`:** millisecond precision is enough for audit and sorting (ties are broken by `Id`), and it is 7
  bytes vs 8 for `datetime2(7)`. `datetimeoffset` is unnecessary because all values are stored in UTC.
- **Collation:** server default `SQL_Latin1_General_CP1_CI_AS` – case-insensitive comparisons, so search does not
  need `LOWER()` (which would prevent index use).

### Isolation level – Read Committed Snapshot (RCSI)

EF Core 10 enables `READ_COMMITTED_SNAPSHOT` when it creates the database (verified:
`sys.databases.is_read_committed_snapshot_on = 1`). Readers see the last committed version of a row instead of
waiting for writers' locks, so the heavy list/summary reads never block status updates and vice versa. This does
not weaken concurrency control for writes: the `rowversion` check is part of the `UPDATE` statement itself, which
always works on the current row version.

### Audit row is written with the status change

The `Tickets` update and the `TicketStatusHistory` insert are saved in the same `SaveChanges` call (one transaction).
If the concurrency check fails, neither is written. Tickets are never deleted, so the FK uses `NO ACTION` – an
audit trail must not disappear through a cascade.

## Index plan

The initial migration created only primary keys (and the FK index EF creates for `RequestId`). Secondary indexes
were formulated as hypotheses, measured on 100k rows, and the final set was added by the `AddIndexes` migration:

| Index | Purpose |
|---|---|
| `IX_Tickets_CreatedAt` | Default order, date ranges |
| `IX_Tickets_Status_Priority_CreatedAt` | Status / priority filters, summary aggregations |
| `IX_Tickets_AssignedTo_CreatedAt` INCLUDE (`Status`) | Assignee + date range, top assignees by open tickets |
| `IX_Tickets_Title` | Prefix search, sort by title |
| `IX_Tickets_OrganizationName` | Prefix search, organization filter |
| `IX_TicketStatusHistory_RequestId` (FK) | History of a ticket |

Rejected after measurement: a `(RequestId, ChangedAt DESC)` history index, an `UpdatedAt` index, covering indexes.
Full measurements and reasoning: [PERFORMANCE.md](PERFORMANCE.md).
