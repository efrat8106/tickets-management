# Performance

> Status: **hypotheses only.** Nothing below has been measured yet. Measurements (T-30, T-31) will replace this
> section with real `STATISTICS IO/TIME` results and execution plans. Every hypothesis will be marked
> confirmed / rejected.

## Query scenarios

| # | Scenario | Shape |
|---|---|---|
| Q1 | Default list | no filter, `ORDER BY CreatedAt DESC, Id DESC`, `OFFSET/FETCH` 20 rows + `COUNT(*)` |
| Q2 | Filter by status (+ priority), sorted by date | `WHERE Status IN (…) [AND Priority IN (…)] ORDER BY CreatedAt DESC` |
| Q3 | Filter by assignee | `WHERE AssignedTo = @a [AND Status IN (…)]` |
| Q4 | Prefix search | `WHERE Title LIKE @t + '%' OR OrganizationName LIKE @t + '%'` |
| Q5 | Date range | `WHERE CreatedAt BETWEEN @from AND @to` |
| Q6 | Summary | `GROUP BY Status`, `GROUP BY Priority`, top 10 `AssignedTo` where `Status <> Completed` |
| Q7 | History of one ticket | `WHERE RequestId = @id ORDER BY ChangedAt DESC` |

## Index hypotheses (to be validated by measurement)

| # | Hypothesis index | Supports | Expected benefit | Risk / cost |
|---|---|---|---|---|
| H1 | `IX_Tickets_CreatedAt (CreatedAt)` | Q1, Q5 | Top-N by date read in index order (backward scan; `Id` is appended automatically as the clustered key, giving the `Id` tie-breaker for free), range seek for Q5 | Key lookups for the 20 returned rows (cheap) |
| H2 | `IX_Tickets_Status_Priority_CreatedAt (Status, Priority, CreatedAt)` | Q2, Q6 | Seek on status/priority; `COUNT` and `GROUP BY Status/Priority` from a narrow index instead of the table | With multiple statuses, global order by date may still need a sort; possibly `(Status, CreatedAt)` is better – to be compared |
| H3 | `IX_Tickets_AssignedTo_Status (AssignedTo, Status)` | Q3, Q6 (top assignees) | Seek by assignee; the top-assignees aggregation reads only this narrow index | – |
| H4 | `IX_Tickets_Title (Title)` | Q4 | `LIKE 'term%'` becomes an index range seek | 400-byte key; one more index to maintain on insert |
| H5 | `IX_Tickets_OrganizationName (OrganizationName)` | Q4, org filter | Same as H4; also supports exact org filter | With OR between Title and Org, the optimizer must use both (index union) – to be verified |
| H6 | `IX_TicketStatusHistory_RequestId_ChangedAt (RequestId, ChangedAt DESC)` | Q7 | Seek + ordered read, replaces the plain FK index | – |

### Considered and expected to be rejected (to be verified)

- **Covering indexes (`INCLUDE` of all list columns):** the list projection uses almost every column, so a covering
  index would duplicate the table. Paging returns ~20 rows, so key lookups should be cheap.
- **`UpdatedAt` index:** only used for sorting; add only if the measured sort cost justifies it.
- **`%term%` (Contains) search:** cannot use a B-tree index (leading wildcard) → scan. Alternative: Full-Text Search.

### Write cost

Every status update changes `Status`, `UpdatedAt` and `RowVersion`. Indexes that contain `Status` (H2, H3) must be
maintained on every status change – an accepted cost, since reads far outnumber writes in this system.
