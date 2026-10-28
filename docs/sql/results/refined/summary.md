# Measurement: refined

Measured 2026-10-28 04:49 on SQL Server 16.0.4305.5 (Docker), 100000 tickets. Warm = median of 5 runs.

| Query | Logical reads | Cold: physical + read-ahead reads | Cold elapsed (ms) | Warm CPU (ms) | Warm elapsed (ms) | Warm runs (ms) |
|---|---:|---:|---:|---:|---:|---|
| Q1-default-list | 282 | 1547 | 45 | 11 | 12 | 12/21/7/12/25 |
| Q2-status-priority-filter | 694 | 1665 | 26 | 2 | 3 | 3/5/3/2/3 |
| Q3-assignee-date-range | 1964 | 2793 | 38 | 3 | 4 | 4/4/3/1/6 |
| Q4-search-prefix | 862 | 1766 | 33 | 4 | 6 | 10/9/6/5/3 |
| Q5-organization-sorted-by-updated | 1983 | 2016 | 29 | 21 | 25 | 18/55/74/21/25 |
| Q6-summary | 988 | 811 | 102 | 49 | 50 | 44/50/40/62/91 |
| Q7-history | 8 | 4 | 2 | 0 | 0 | 0/0/0/0/0 |
| Q8-deep-page | 2073 | 2000 | 408 | 688 | 273 | 229/317/273/251/301 |
| Q9-sort-by-updated | 2073 | 2000 | 74 | 304 | 42 | 81/42/40/40/42 |

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
