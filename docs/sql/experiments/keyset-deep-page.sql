-- Experiment: the same deep page as Q8 (rows 79,981–80,000 of the default order), fetched with keyset pagination
-- instead of OFFSET. Not implemented in the API – measured to support the improvement proposal in PERFORMANCE.md.
SET NOCOUNT ON;

-- The "cursor" a client would hold: CreatedAt and Id of the last row of the previous page (row 79,980).
DECLARE @lastCreatedAt datetime2(3), @lastId int;
SELECT @lastCreatedAt = CreatedAt, @lastId = Id
FROM Tickets
ORDER BY CreatedAt DESC, Id DESC
OFFSET 79979 ROWS FETCH NEXT 1 ROWS ONLY;

SET STATISTICS IO, TIME ON;

EXEC sp_executesql N'SELECT TOP (@pageSize) [t].[Id], [t].[Title], [t].[OrganizationName], [t].[Status], [t].[Priority], [t].[AssignedTo], [t].[CreatedAt], [t].[UpdatedAt], [t].[RowVersion]
FROM [Tickets] AS [t]
WHERE [t].[CreatedAt] < @lastCreatedAt OR ([t].[CreatedAt] = @lastCreatedAt AND [t].[Id] < @lastId)
ORDER BY [t].[CreatedAt] DESC, [t].[Id] DESC', N'@pageSize int, @lastCreatedAt datetime2(3), @lastId int', @pageSize = 20, @lastCreatedAt = @lastCreatedAt, @lastId = @lastId;

SET STATISTICS IO, TIME OFF;

-- Check: keyset page must contain exactly the same rows as the OFFSET page (Q8).
SELECT CASE WHEN EXISTS (
    (SELECT TOP (20) Id FROM Tickets WHERE CreatedAt < @lastCreatedAt OR (CreatedAt = @lastCreatedAt AND Id < @lastId) ORDER BY CreatedAt DESC, Id DESC)
    EXCEPT
    (SELECT Id FROM Tickets ORDER BY CreatedAt DESC, Id DESC OFFSET 79980 ROWS FETCH NEXT 20 ROWS ONLY))
  THEN 'DIFFERENT' ELSE 'SAME ROWS AS OFFSET PAGE' END AS KeysetCheck;
