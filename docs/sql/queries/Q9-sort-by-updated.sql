-- Q9: GET /api/tickets?sortBy=updatedAt (no filter, page 1) – used to decide on an UpdatedAt index. Exact EF SQL.
SET NOCOUNT ON;
SET STATISTICS IO, TIME ON;

EXEC sp_executesql N'SELECT [t].[Id], [t].[Title], [t].[OrganizationName], [t].[Status], [t].[Priority], [t].[AssignedTo], [t].[CreatedAt], [t].[UpdatedAt], [t].[RowVersion]
FROM [Tickets] AS [t]
ORDER BY [t].[UpdatedAt] DESC, [t].[Id] DESC
OFFSET @p ROWS FETCH NEXT @p1 ROWS ONLY', N'@p int, @p1 int', @p = 0, @p1 = 20;
