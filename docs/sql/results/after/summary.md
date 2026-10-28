# Measurement: after

Measured 2026-10-28 04:52 on SQL Server 16.0.4305.5 (Docker), 100000 tickets. Warm = median of 5 runs.

| Query | Logical reads | Cold: physical + read-ahead reads | Cold elapsed (ms) | Warm CPU (ms) | Warm elapsed (ms) | Warm runs (ms) |
|---|---:|---:|---:|---:|---:|---|
| Q1-default-list | 282 | 1477 | 84 | 13 | 12 | 10/9/12/22/27 |
| Q2-status-priority-filter | 694 | 1665 | 94 | 4 | 6 | 3/2/6/6/7 |
| Q3-assignee-date-range | 1964 | 2793 | 108 | 2 | 3 | 4/3/2/6/2 |
| Q4-search-prefix | 862 | 1766 | 78 | 7 | 7 | 7/4/4/7/9 |
| Q5-organization-sorted-by-updated | 1983 | 2016 | 49 | 20 | 21 | 21/17/20/32/40 |
| Q6-summary | 988 | 811 | 81 | 65 | 63 | 63/59/91/40/114 |
| Q7-history | 8 | 4 | 3 | 0 | 0 | 0/0/0/0/0 |
| Q8-deep-page | 2073 | 2000 | 327 | 821 | 308 | 319/235/336/205/308 |
| Q9-sort-by-updated | 2073 | 2000 | 72 | 468 | 68 | 68/70/44/55/71 |

## Actual plan operators

- **Q1-default-list**: stmt 1: Compute Scalar > Stream Aggregate > Index Scan[IX_Tickets_CreatedAt] | stmt 2: Top > Nested Loops > Index Scan[IX_Tickets_CreatedAt] > Clustered Index Seek[PK_Tickets]
- **Q2-status-priority-filter**: stmt 1: Compute Scalar > Stream Aggregate > Nested Loops > Merge Interval > Sort > Concatenation > Constant Scan > Index Seek[IX_Tickets_Status_Priority_CreatedAt] | stmt 2: Top > Nested Loops > Index Scan[IX_Tickets_CreatedAt] > Clustered Index Seek[PK_Tickets]
- **Q3-assignee-date-range**: stmt 1: Compute Scalar > Stream Aggregate > Index Seek[IX_Tickets_AssignedTo_CreatedAt] | stmt 2: Top > Nested Loops > Index Scan[IX_Tickets_Title] > Clustered Index Seek[PK_Tickets]
- **Q4-search-prefix**: stmt 1: Compute Scalar > Stream Aggregate > Hash Match > Concatenation > Nested Loops > Constant Scan > Index Seek[IX_Tickets_Title] > Index Seek[IX_Tickets_OrganizationName] | stmt 2: Top > Nested Loops > Index Scan[IX_Tickets_CreatedAt] > Clustered Index Seek[PK_Tickets]
- **Q5-organization-sorted-by-updated**: stmt 1: Compute Scalar > Stream Aggregate > Index Seek[IX_Tickets_OrganizationName] | stmt 2: Top > Sort > Clustered Index Scan[PK_Tickets]
- **Q6-summary**: stmt 1: Compute Scalar > Stream Aggregate > Index Scan[IX_Tickets_Status_Priority_CreatedAt] | stmt 2: Compute Scalar > Hash Match > Index Scan[IX_Tickets_Status_Priority_CreatedAt] | stmt 3: Top > Sort > Compute Scalar > Stream Aggregate > Index Seek[IX_Tickets_AssignedTo_CreatedAt]
- **Q7-history**: stmt 1: Compute Scalar > Stream Aggregate > Index Seek[IX_TicketStatusHistory_RequestId] | stmt 2: Top > Sort > Nested Loops > Index Seek[IX_TicketStatusHistory_RequestId] > Clustered Index Seek[PK_TicketStatusHistory]
- **Q8-deep-page**: stmt 1: Top > Parallelism > Sort > Clustered Index Scan[PK_Tickets]
- **Q9-sort-by-updated**: stmt 1: Top > Parallelism > Sort > Clustered Index Scan[PK_Tickets]
