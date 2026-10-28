-- Q2: GET /api/tickets?status=New&status=Waiting&priority=High (sorted by createdAt desc, page 1). Exact EF SQL.
SET NOCOUNT ON;
SET STATISTICS IO, TIME ON;

EXEC sp_executesql N'SELECT COUNT(*)
FROM [Tickets] AS [t]
WHERE [t].[Status] IN (@statuses1, @statuses2) AND [t].[Priority] = @priorities1', N'@statuses1 tinyint, @statuses2 tinyint, @priorities1 tinyint', @statuses1 = 1, @statuses2 = 3, @priorities1 = 3;

EXEC sp_executesql N'SELECT [t].[Id], [t].[Title], [t].[OrganizationName], [t].[Status], [t].[Priority], [t].[AssignedTo], [t].[CreatedAt], [t].[UpdatedAt], [t].[RowVersion]
FROM [Tickets] AS [t]
WHERE [t].[Status] IN (@statuses1, @statuses2) AND [t].[Priority] = @priorities1
ORDER BY [t].[CreatedAt] DESC, [t].[Id] DESC
OFFSET @p ROWS FETCH NEXT @p3 ROWS ONLY', N'@statuses1 tinyint, @statuses2 tinyint, @priorities1 tinyint, @p int, @p3 int', @statuses1 = 1, @statuses2 = 3, @priorities1 = 3, @p = 0, @p3 = 20;
