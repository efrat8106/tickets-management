-- Q3: GET /api/tickets?assignedTo=dana.cohen&createdFrom=2026-01-01&createdTo=2026-03-31 (sorted by title asc). Exact EF SQL.
SET NOCOUNT ON;
SET STATISTICS IO, TIME ON;

EXEC sp_executesql N'SELECT COUNT(*)
FROM [Tickets] AS [t]
WHERE [t].[AssignedTo] = @assignee AND [t].[CreatedAt] >= @fromUtc AND [t].[CreatedAt] <= @toUtc', N'@assignee nvarchar(100), @fromUtc datetime2(3), @toUtc datetime2(3)', @assignee = N'dana.cohen', @fromUtc = '2026-01-01T00:00:00.000', @toUtc = '2026-03-31T23:59:59.999';

EXEC sp_executesql N'SELECT [t].[Id], [t].[Title], [t].[OrganizationName], [t].[Status], [t].[Priority], [t].[AssignedTo], [t].[CreatedAt], [t].[UpdatedAt], [t].[RowVersion]
FROM [Tickets] AS [t]
WHERE [t].[AssignedTo] = @assignee AND [t].[CreatedAt] >= @fromUtc AND [t].[CreatedAt] <= @toUtc
ORDER BY [t].[Title], [t].[Id]
OFFSET @p ROWS FETCH NEXT @p4 ROWS ONLY', N'@assignee nvarchar(100), @fromUtc datetime2(3), @toUtc datetime2(3), @p int, @p4 int', @assignee = N'dana.cohen', @fromUtc = '2026-01-01T00:00:00.000', @toUtc = '2026-03-31T23:59:59.999', @p = 0, @p4 = 20;
