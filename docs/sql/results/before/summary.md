# Measurement: before

Measured 2026-10-28 04:45 on SQL Server 16.0.4305.5 (Docker), 100000 tickets. Warm = median of 5 runs.

| Query | Logical reads | Cold: physical + read-ahead reads | Cold elapsed (ms) | Warm CPU (ms) | Warm elapsed (ms) | Warm runs (ms) |
|---|---:|---:|---:|---:|---:|---|
| Q1-default-list | 4046 | 2000 | 100 | 289 | 53 | 44/48/74/120/53 |
| Q2-status-priority-filter | 3946 | 2000 | 108 | 42 | 44 | 44/49/30/34/100 |
| Q3-assignee-date-range | 3946 | 2000 | 71 | 49 | 48 | 83/48/109/48/37 |
| Q4-search-prefix | 3946 | 2000 | 93 | 69 | 71 | 61/71/106/69/103 |
| Q5-organization-sorted-by-updated | 3946 | 2000 | 46 | 55 | 54 | 37/42/54/54/56 |
| Q6-summary | 5919 | 2000 | 214 | 116 | 118 | 143/71/118/80/196 |
| Q7-history | 8 | 4 | 7 | 0 | 0 | 0/0/0/0/0 |
| Q8-deep-page | 2073 | 2000 | 244 | 780 | 316 | 405/211/238/316/325 |
| Q9-sort-by-updated | 2073 | 2000 | 64 | 433 | 57 | 62/54/57/64/42 |

## Actual plan operators

- **Q1-default-list**: stmt 1: Compute Scalar > Stream Aggregate > Clustered Index Scan[PK_Tickets] | stmt 2: Top > Parallelism > Sort > Clustered Index Scan[PK_Tickets]
- **Q2-status-priority-filter**: stmt 1: Compute Scalar > Stream Aggregate > Clustered Index Scan[PK_Tickets] | stmt 2: Top > Sort > Clustered Index Scan[PK_Tickets]
- **Q3-assignee-date-range**: stmt 1: Compute Scalar > Stream Aggregate > Clustered Index Scan[PK_Tickets] | stmt 2: Top > Sort > Clustered Index Scan[PK_Tickets]
- **Q4-search-prefix**: stmt 1: Compute Scalar > Stream Aggregate > Clustered Index Scan[PK_Tickets] | stmt 2: Top > Sort > Clustered Index Scan[PK_Tickets]
- **Q5-organization-sorted-by-updated**: stmt 1: Compute Scalar > Stream Aggregate > Clustered Index Scan[PK_Tickets] | stmt 2: Top > Sort > Clustered Index Scan[PK_Tickets]
- **Q6-summary**: stmt 1: Compute Scalar > Hash Match > Clustered Index Scan[PK_Tickets] | stmt 2: Compute Scalar > Hash Match > Clustered Index Scan[PK_Tickets] | stmt 3: Top > Sort > Compute Scalar > Hash Match > Clustered Index Scan[PK_Tickets]
- **Q7-history**: stmt 1: Compute Scalar > Stream Aggregate > Index Seek[IX_TicketStatusHistory_RequestId] | stmt 2: Top > Sort > Nested Loops > Index Seek[IX_TicketStatusHistory_RequestId] > Clustered Index Seek[PK_TicketStatusHistory]
- **Q8-deep-page**: stmt 1: Top > Parallelism > Sort > Clustered Index Scan[PK_Tickets]
- **Q9-sort-by-updated**: stmt 1: Top > Parallelism > Sort > Clustered Index Scan[PK_Tickets]
