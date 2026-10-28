-- Q5: GET /api/tickets?organizationName=שקד בנייה&sortBy=updatedAt (a small organization, exact match). Exact EF SQL.
SET NOCOUNT ON;
SET STATISTICS IO, TIME ON;

EXEC sp_executesql N'SELECT COUNT(*)
FROM [Tickets] AS [t]
WHERE [t].[OrganizationName] = @organization', N'@organization nvarchar(200)', @organization = N'שקד בנייה';

EXEC sp_executesql N'SELECT [t].[Id], [t].[Title], [t].[OrganizationName], [t].[Status], [t].[Priority], [t].[AssignedTo], [t].[CreatedAt], [t].[UpdatedAt], [t].[RowVersion]
FROM [Tickets] AS [t]
WHERE [t].[OrganizationName] = @organization
ORDER BY [t].[UpdatedAt] DESC, [t].[Id] DESC
OFFSET @p ROWS FETCH NEXT @p1 ROWS ONLY', N'@organization nvarchar(200), @p int, @p1 int', @organization = N'שקד בנייה', @p = 0, @p1 = 20;
