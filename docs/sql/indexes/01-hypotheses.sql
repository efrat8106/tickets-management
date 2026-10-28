-- Experiment 1: the index hypotheses H1–H6 from docs/PERFORMANCE.md, exactly as formulated before measuring.
-- Applied by hand for measurement only; the final set is created by the EF migration AddIndexes.
SET NOCOUNT ON;

CREATE INDEX IX_Tickets_CreatedAt ON Tickets (CreatedAt);                                  -- H1
CREATE INDEX IX_Tickets_Status_Priority_CreatedAt ON Tickets (Status, Priority, CreatedAt); -- H2
CREATE INDEX IX_Tickets_AssignedTo_Status ON Tickets (AssignedTo, Status);                 -- H3
CREATE INDEX IX_Tickets_Title ON Tickets (Title);                                          -- H4
CREATE INDEX IX_Tickets_OrganizationName ON Tickets (OrganizationName);                    -- H5

DROP INDEX IX_TicketStatusHistory_RequestId ON TicketStatusHistory;                        -- H6 replaces the FK index
CREATE INDEX IX_TicketStatusHistory_RequestId_ChangedAt ON TicketStatusHistory (RequestId, ChangedAt DESC);
