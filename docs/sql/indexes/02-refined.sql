-- Experiment 2 (applied on top of experiment 1), based on the measured results:
--  * H3 (AssignedTo, Status) replaced by (AssignedTo, CreatedAt) INCLUDE (Status):
--    the assignee + date range COUNT became an intersection of two indexes with H3; with CreatedAt as second key it
--    is a single range seek, and Status as an included column still covers the "top assignees by open tickets" query.
--  * H6 rejected: (RequestId, ChangedAt DESC) gave no measurable gain over the FK index (8 logical reads, 0 ms in
--    both cases; the plan still sorts because of the Id tie-breaker). Back to the FK index EF creates.
SET NOCOUNT ON;

DROP INDEX IX_Tickets_AssignedTo_Status ON Tickets;
CREATE INDEX IX_Tickets_AssignedTo_CreatedAt ON Tickets (AssignedTo, CreatedAt) INCLUDE (Status);

DROP INDEX IX_TicketStatusHistory_RequestId_ChangedAt ON TicketStatusHistory;
CREATE INDEX IX_TicketStatusHistory_RequestId ON TicketStatusHistory (RequestId);
