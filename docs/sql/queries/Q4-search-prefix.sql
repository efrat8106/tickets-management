-- Q4: GET /api/tickets?search=Invoice – prefix search on Title OR OrganizationName (sorted by createdAt desc). Exact EF SQL.
SET NOCOUNT ON;
SET STATISTICS IO, TIME ON;

EXEC sp_executesql N'SELECT COUNT(*)
FROM [Tickets] AS [t]
WHERE [t].[Title] LIKE @term_startswith ESCAPE N''\'' OR [t].[OrganizationName] LIKE @term_startswith ESCAPE N''\''', N'@term_startswith nvarchar(200)', @term_startswith = N'Invoice%';

EXEC sp_executesql N'SELECT [t].[Id], [t].[Title], [t].[OrganizationName], [t].[Status], [t].[Priority], [t].[AssignedTo], [t].[CreatedAt], [t].[UpdatedAt], [t].[RowVersion]
FROM [Tickets] AS [t]
WHERE [t].[Title] LIKE @term_startswith ESCAPE N''\'' OR [t].[OrganizationName] LIKE @term_startswith ESCAPE N''\''
ORDER BY [t].[CreatedAt] DESC, [t].[Id] DESC
OFFSET @p ROWS FETCH NEXT @p1 ROWS ONLY', N'@term_startswith nvarchar(200), @p int, @p1 int', @term_startswith = N'Invoice%', @p = 0, @p1 = 20;
