-- Removes the experiment indexes so the database matches the InitialCreate migration again;
-- the final index set is then created by the EF migration AddIndexes.
SET NOCOUNT ON;

DROP INDEX IF EXISTS IX_Tickets_CreatedAt ON Tickets;
DROP INDEX IF EXISTS IX_Tickets_Status_Priority_CreatedAt ON Tickets;
DROP INDEX IF EXISTS IX_Tickets_AssignedTo_Status ON Tickets;
DROP INDEX IF EXISTS IX_Tickets_AssignedTo_CreatedAt ON Tickets;
DROP INDEX IF EXISTS IX_Tickets_Title ON Tickets;
DROP INDEX IF EXISTS IX_Tickets_OrganizationName ON Tickets;
