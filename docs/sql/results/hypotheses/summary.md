# Measurement: hypotheses

Measured 2026-10-28 04:47 on SQL Server 16.0.4305.5 (Docker), 100000 tickets. Warm = median of 5 runs.

| Query | Logical reads | Cold: physical + read-ahead reads | Cold elapsed (ms) | Warm CPU (ms) | Warm elapsed (ms) | Warm runs (ms) |
|---|---:|---:|---:|---:|---:|---|
| Q1-default-list | 282 | 1547 | 69 | 11 | 14 | 14/12/22/11/18 |
| Q2-status-priority-filter | 694 | 1665 | 86 | 3 | 3 | 4/3/1/2/4 |
| Q3-assignee-date-range | 2068 | 2924 | 80 | 12 | 13 | 24/13/15/10/13 |
| Q4-search-prefix | 862 | 1766 | 38 | 5 | 9 | 3/10/5/9/12 |
| Q5-organization-sorted-by-updated | 1983 | 2016 | 61 | 33 | 34 | 37/23/15/34/36 |
| Q6-summary | 906 | 729 | 49 | 56 | 62 | 47/97/63/37/62 |
| Q7-history | 8 | 4 | 3 | 0 | 0 | 0/0/0/0/0 |
| Q8-deep-page | 2073 | 2000 | 381 | 691 | 273 | 401/268/210/413/273 |
| Q9-sort-by-updated | 2073 | 2000 | 56 | 337 | 56 | 59/41/73/56/42 |

## Actual plan operators

- **Q1-default-list**: stmt 1: Compute Scalar > Stream Aggregate > Index Scan[IX_Tickets_CreatedAt] | stmt 2: Top > Nested Loops > Index Scan[IX_Tickets_CreatedAt] > Clustered Index Seek[PK_Tickets]
- **Q2-status-priority-filter**: stmt 1: Compute Scalar > Stream Aggregate > Nested Loops > Merge Interval > Sort > Concatenation > Constant Scan > Index Seek[IX_Tickets_Status_Priority_CreatedAt] | stmt 2: Top > Nested Loops > Index Scan[IX_Tickets_CreatedAt] > Clustered Index Seek[PK_Tickets]
- **Q3-assignee-date-range**: stmt 1: Compute Scalar > Stream Aggregate > Hash Match > Index Seek[IX_Tickets_CreatedAt] > Index Seek[IX_Tickets_AssignedTo_Status] | stmt 2: Top > Nested Loops > Index Scan[IX_Tickets_Title] > Clustered Index Seek[PK_Tickets]
- **Q4-search-prefix**: stmt 1: Compute Scalar > Stream Aggregate > Hash Match > Concatenation > Nested Loops > Constant Scan > Index Seek[IX_Tickets_Title] > Index Seek[IX_Tickets_OrganizationName] | stmt 2: Top > Nested Loops > Index Scan[IX_Tickets_CreatedAt] > Clustered Index Seek[PK_Tickets]
- **Q5-organization-sorted-by-updated**: stmt 1: Compute Scalar > Stream Aggregate > Index Seek[IX_Tickets_OrganizationName] | stmt 2: Top > Sort > Clustered Index Scan[PK_Tickets]
- **Q6-summary**: stmt 1: Compute Scalar > Stream Aggregate > Index Scan[IX_Tickets_Status_Priority_CreatedAt] | stmt 2: Compute Scalar > Hash Match > Index Scan[IX_Tickets_Status_Priority_CreatedAt] | stmt 3: Top > Sort > Compute Scalar > Stream Aggregate > Index Seek[IX_Tickets_AssignedTo_Status]
- **Q7-history**: stmt 1: Compute Scalar > Stream Aggregate > Index Seek[IX_TicketStatusHistory_RequestId_ChangedAt] | stmt 2: Top > Sort > Nested Loops > Index Seek[IX_TicketStatusHistory_RequestId_ChangedAt] > Clustered Index Seek[PK_TicketStatusHistory]
- **Q8-deep-page**: stmt 1: Top > Parallelism > Sort > Clustered Index Scan[PK_Tickets]
- **Q9-sort-by-updated**: stmt 1: Top > Parallelism > Sort > Clustered Index Scan[PK_Tickets]
