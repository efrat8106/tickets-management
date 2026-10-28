-- Q8: GET /api/tickets?page=4000 – a deep page of the default list, to show the cost of OFFSET paging. Exact EF SQL.
SET NOCOUNT ON;
SET STATISTICS IO, TIME ON;

EXEC sp_executesql N'SELECT [t].[Id], [t].[Title], [t].[OrganizationName], [t].[Status], [t].[Priority], [t].[AssignedTo], [t].[CreatedAt], [t].[UpdatedAt], [t].[RowVersion]
FROM [Tickets] AS [t]
ORDER BY [t].[CreatedAt] DESC, [t].[Id] DESC
OFFSET @p ROWS FETCH NEXT @p1 ROWS ONLY', N'@p int, @p1 int', @p = 79980, @p1 = 20;
