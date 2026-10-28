# Performance

All numbers below were measured on the seeded data set (100,000 tickets, 17,151 history rows, `seed --reset`) –
nothing is estimated. Scripts, raw `STATISTICS IO/TIME` output and actual execution plans are in [`docs/sql/`](sql/).

## How it was measured

- **Environment:** SQL Server 2022 (16.0.4305.5) in Docker Desktop on a Windows 11 laptop; API and Angular stopped
  during measurement.
- **Queries:** [`docs/sql/queries/`](sql/queries/) – the exact SQL EF Core generates for each scenario (copied from the
  EF log), sent through `sp_executesql` with the same parameter types, so plans and parameter sniffing match the
  application.
- **Harness:** [`docs/sql/measure.ps1`](sql/measure.ps1). Per scenario:
  - **cold** – `CHECKPOINT; DBCC DROPCLEANBUFFERS; DBCC FREEPROCCACHE`, then one run (data from disk, plan compiled);
  - **warm** – 5 runs in a row; the **median** is reported and all five values are kept;
  - **plan** – one run with `SET STATISTICS XML ON`; actual plans saved as `.sqlplan` (open in SSMS / Azure Data Studio).
- **Metrics:** logical reads (8 KB pages read from memory) are the **stable** metric – identical between runs.
  Elapsed times at this scale are a few milliseconds and vary between runs (e.g. 9–27 ms for Q1 after indexing);
  cold timings additionally include compilation and disk reads in a Docker VM and are the noisiest – they are kept
  in the raw results but not used for decisions.
- **Runs:** [`results/before`](sql/results/before/summary.md) (PK + FK index only) →
  [`results/hypotheses`](sql/results/hypotheses/summary.md) (H1–H6 as formulated) →
  [`results/refined`](sql/results/refined/summary.md) (after one correction) →
  [`results/after`](sql/results/after/summary.md) (final set, created by the `AddIndexes` migration).

## Scenarios

| # | Scenario (API call) | Statements |
|---|---|---|
| Q1 | Default list `GET /api/tickets` | COUNT + page 1 (ORDER BY CreatedAt DESC, Id DESC) |
| Q2 | `status=New&status=Waiting&priority=High` | COUNT + page 1 |
| Q3 | `assignedTo=dana.cohen` + 3-month date range, `sortBy=title` | COUNT + page 1 |
| Q4 | `search=Invoice` (prefix on Title OR OrganizationName) | COUNT + page 1 |
| Q5 | `organizationName=שקד בנייה` (1,033 tickets), `sortBy=updatedAt` | COUNT + page 1 |
| Q6 | `GET /api/tickets/summary` (cache miss) | 3 × GROUP BY |
| Q7 | `GET /api/tickets/4/history` | COUNT + page |
| Q8 | Deep page: page 4,000 of the default list (`OFFSET 79980`) | page only |
| Q9 | `sortBy=updatedAt`, no filter | page only |

## Results: before → after

Warm medians. "Before" = only the primary keys and the FK index on `TicketStatusHistory.RequestId`.

| Query | Logical reads before | Logical reads after | Change | Warm elapsed before (ms) | Warm elapsed after (ms) |
|---|---:|---:|---:|---:|---:|
| Q1 default list | 4,046 | 282 | −93% | 53 | 12 |
| Q2 status + priority | 3,946 | 694 | −82% | 44 | 6 |
| Q3 assignee + date range | 3,946 | 1,964 | −50% | 48 | 3 |
| Q4 prefix search | 3,946 | 862 | −78% | 71 | 7 |
| Q5 organization, sort by updatedAt | 3,946 | 1,983 | −50% | 54 | 21 |
| Q6 summary (3 aggregations) | 5,919 | 988 | −83% | 118 | 63 |
| Q7 history | 8 | 8 | 0 | 0 | 0 |
| Q8 deep page (OFFSET 79,980) | 2,073 | 2,073 | 0 | 316 | 308 |
| Q9 sort by updatedAt | 2,073 | 2,073 | 0 | 57 | 68 |

Per statement (first warm run; worktable lines omitted):

| Query | Before: COUNT + page | After: COUNT + page |
|---|---|---|
| Q1 | 1,973 + 2,073 | 218 + 82 |
| Q2 | 1,973 + 1,973 | 49 + 673 |
| Q3 | 1,973 + 1,973 | 18 + 1,946 |
| Q4 | 1,973 + 1,973 | 51 + 839 |
| Q5 | 1,973 + 1,973 | 10 + 1,973 |
| Q6 (status / priority / top assignees) | 1,973 / 1,973 / 1,973 | 238 / 238 / 512 |

### Execution plans (actual), before → after

| Query | Before | After |
|---|---|---|
| Q1 | COUNT: Clustered Index Scan · page: Clustered Index Scan → Sort (parallel) → Top | COUNT: Index Scan `IX_Tickets_CreatedAt` (230 pages instead of 1,973) · page: ordered Index Scan `IX_Tickets_CreatedAt` → 20 Key Lookups → Top – **no sort** |
| Q2 | Clustered Index Scan (+ Sort) | COUNT: two Index Seeks on `IX_Tickets_Status_Priority_CreatedAt` (one per status) · page: ordered scan of `IX_Tickets_CreatedAt` with lookups until 20 matches |
| Q3 | Clustered Index Scan (+ Sort) | COUNT: one range Seek on `IX_Tickets_AssignedTo_CreatedAt` · page: ordered scan of `IX_Tickets_Title` with lookups |
| Q4 | Clustered Index Scan (+ Sort) | COUNT: Index Seeks on `IX_Tickets_Title` and `IX_Tickets_OrganizationName` (LIKE 'Invoice%' is a range seek) · page: ordered scan of `IX_Tickets_CreatedAt` |
| Q5 | Clustered Index Scan (+ Sort) | COUNT: Index Seek `IX_Tickets_OrganizationName` · page: still Clustered Index Scan → Sort (see bottlenecks) |
| Q6 | 3 × Clustered Index Scan + Hash aggregate | GROUP BY Status: Stream Aggregate over `IX_Tickets_Status_Priority_CreatedAt` (already ordered by Status) · GROUP BY Priority: scan of the same narrow index · top assignees: Seek on `IX_Tickets_AssignedTo_CreatedAt` (skips NULLs; Status from the included column) |
| Q8 / Q9 | Clustered Index Scan → Sort → Top | unchanged (the optimizer correctly prefers it – see below) |

## Index decisions

Hypotheses were written before measuring (T-06) and are confirmed, changed or rejected here.

| # | Hypothesis | Verdict | Evidence |
|---|---|---|---|
| H1 | `IX_Tickets_CreatedAt (CreatedAt)` | **Kept** | Q1 4,046 → 282 reads, the page query no longer sorts; also used by the page queries of Q2 and Q4 and for the COUNT of the unfiltered list. `Id` (clustered key) is appended automatically, so the index order already includes the tie-breaker. |
| H2 | `IX_Tickets_Status_Priority_CreatedAt (Status, Priority, CreatedAt)` | **Kept** | Q2 COUNT 1,973 → 49 reads; Q6 GROUP BY Status / Priority 1,973 → 238 reads each (narrow index, Stream Aggregate for Status). |
| H3 | `IX_Tickets_AssignedTo_Status (AssignedTo, Status)` | **Changed** → `(AssignedTo, CreatedAt) INCLUDE (Status)` | With H3, Q3's COUNT needed a hash intersection of two indexes (AssignedTo seek + CreatedAt seek). With CreatedAt as the second key it is a single range seek (18 reads); Q3 warm 13 → 4 ms between the two experiments. `Status` as an included column still covers the top-assignees aggregation (Q6: 512 reads, seek that skips unassigned rows). |
| H4 | `IX_Tickets_Title (Title)` | **Kept** | `LIKE 'term%'` on Title becomes a range seek (Q4 COUNT 1,973 → 51 together with H5); also gives the order for `sortBy=title` (Q3 page). |
| H5 | `IX_Tickets_OrganizationName (OrganizationName)` | **Kept** | Needed for the OR branch of the prefix search (without it the OR forces a scan) and for the exact organization filter (Q5 COUNT 1,973 → 10). |
| H6 | `IX_TicketStatusHistory (RequestId, ChangedAt DESC)` replacing the FK index | **Rejected** | No measurable difference: 8 logical reads and 0 ms with either index, and the plan still sorts (the `Id DESC` tie-breaker is not in that order). The FK index EF creates anyway is sufficient; a ticket has only a handful of history rows. |
| – | Index on `UpdatedAt` | **Rejected** | Q9 (sort by updatedAt, no filter) costs 2,073 reads / ~57–68 ms with a scan + sort – acceptable for a secondary sort option. `UpdatedAt` changes on **every** status update, so such an index would be maintained on every write. |
| – | Covering indexes (`INCLUDE` all list columns) | **Rejected** | The list returns almost every column (incl. two 200-character strings), so a covering index would roughly duplicate the table (1,973 pages) per index. The page queries need only ~20 key lookups when the order comes from an index (Q1: 82 reads). |
| – | `%term%` (contains) search | **Not supported by design** | A leading wildcard cannot use a B-tree index and always scans. Search is prefix-only; Full-Text Search is the alternative (see below). |

### Final indexes and their cost

Created by the EF migration `AddIndexes` (configured in `TicketConfiguration`). Sizes measured after creation
([`results/after/index-sizes.txt`](sql/results/after/index-sizes.txt)):

| Index | Pages | MB | Serves |
|---|---:|---:|---|
| `PK_Tickets` (clustered, the table itself) | 1,973 | 15.41 | – |
| `IX_Tickets_CreatedAt` | 230 | 1.80 | Q1, Q2/Q4 pages, date ranges |
| `IX_Tickets_Status_Priority_CreatedAt` | 242 | 1.89 | Q2, Q6 |
| `IX_Tickets_AssignedTo_CreatedAt` (+ Status) | 544 | 4.25 | Q3, Q6 |
| `IX_Tickets_Title` | 830 | 6.48 | Q4, sort by title |
| `IX_Tickets_OrganizationName` | 545 | 4.26 | Q4, Q5 |
| `IX_TicketStatusHistory_RequestId` (FK) | 41 | 0.32 | Q7 |

**Write cost:** the five secondary indexes take 18.7 MB next to the 15.4 MB table. A ticket insert updates all of
them. A **status update** changes `Status`, `UpdatedAt` and `RowVersion`; only the two indexes that contain `Status`
(`IX_Tickets_Status_Priority_CreatedAt`, and `IX_Tickets_AssignedTo_CreatedAt` through its included column) must be
updated – `UpdatedAt` is deliberately not indexed. In this system reads (list, search, summary) far outnumber writes.

## How loading everything into memory is avoided

- **Filtering, sorting and paging run in SQL Server.** `TicketQueryService` composes an `IQueryable` (`Where` per
  filter, `OrderBy` from a whitelist + `Id` tie-breaker, `Skip/Take`) and materializes only the requested page with
  `ToListAsync`. The SQL in `docs/sql/queries` is what EF actually sends: `WHERE … ORDER BY … OFFSET … FETCH`.
- **Projection, not entities:** `Select` into the DTO, so only the listed columns are read, with `AsNoTracking`.
- **Total count** is a separate `SELECT COUNT(*)` over the same filters – a number, not rows. For a page beyond the
  end no page query is sent at all.
- **Aggregations** are `GROUP BY` in SQL; only ~17 numbers are transferred. They are additionally cached (see
  [CACHE.md](CACHE.md)).
- **Bulk update** loads all affected tickets with one `WHERE Id IN (…)` query (max 100).
- **Angular** never filters, sorts or pages locally; every change becomes a new API request.

## Bottlenecks and improvement proposals

1. **Deep pages with OFFSET (Q8) – the main bottleneck.** `OFFSET 79980` has to produce and discard 79,980 rows:
   2,073 reads and ~308 ms warm, unchanged by indexing. (The optimizer rightly does not use `IX_Tickets_CreatedAt`
   here – it would need ~80,000 key lookups.) **Proposal: keyset (seek) pagination** – the client sends the
   `CreatedAt` + `Id` of the last row instead of a page number, and the query becomes
   `WHERE CreatedAt < @lastCreatedAt OR (CreatedAt = @lastCreatedAt AND Id < @lastId) ORDER BY CreatedAt DESC, Id DESC`.
   **Measured** on the same page ([`experiments/keyset-deep-page.sql`](sql/experiments/keyset-deep-page.sql),
   [results](sql/experiments/keyset-deep-page.results.txt)): **65–72 logical reads and 0–3 ms** over 5 runs instead
   of 2,073 reads / ~308 ms, returning exactly the same 20 rows. Trade-off: no "jump to page N", only next/previous –
   which is why it was not implemented in the UI.
2. **Filter + sort on a column that is not in the filter's index (Q3 page, Q5 page).** Q5 filters 1,033 tickets of
   one organization but sorts by `UpdatedAt`: fetching 1,033 rows by key lookup costs more than one scan, so the page
   query still reads 1,973 pages (21 ms). Options: a composite index per common filter + sort combination (e.g.
   `(OrganizationName, CreatedAt)` for the default sort), or limiting sort options. Not added, to keep the write cost
   and the number of indexes proportional to measured needs.
3. **COUNT on every list request.** Even indexed, the unfiltered COUNT reads the narrowest index (218 pages). With
   millions of rows: return the count only on the first page, show "more than N", cache counts for unfiltered views,
   or use keyset pagination without a total.
4. **Contains search.** If users need `%term%` search: SQL Server Full-Text Search (`CONTAINS`) on Title and
   OrganizationName (requires an image with the Full-Text feature).
5. **Summary at larger scale.** It already reads narrow indexes (988 pages) and is cached; for millions of rows an
   indexed view with `COUNT_BIG(*)` grouped by Status / Priority would make it near-constant.

## Reproduce

```powershell
dotnet run --project src/Tickets.Api -- seed --count 100000 --reset   # same deterministic data
./docs/sql/measure.ps1 -Label after                                    # writes docs/sql/results/after
```
The "before" state can be reproduced by dropping the five `IX_Tickets_*` indexes
([`indexes/03-revert-experiments.sql`](sql/indexes/03-revert-experiments.sql)); the experiments are in
[`docs/sql/indexes`](sql/indexes/).
