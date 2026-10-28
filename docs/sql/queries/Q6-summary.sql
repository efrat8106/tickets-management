-- Q6: GET /api/tickets/summary – the three aggregations (computed on a cache miss). Exact EF SQL.
SET NOCOUNT ON;
SET STATISTICS IO, TIME ON;

SELECT [t].[Status], COUNT(*) AS [Count]
FROM [Tickets] AS [t]
GROUP BY [t].[Status];

SELECT [t].[Priority], COUNT(*) AS [Count]
FROM [Tickets] AS [t]
GROUP BY [t].[Priority];

EXEC sp_executesql N'SELECT TOP(@p) [t].[AssignedTo], COUNT(*)
FROM [Tickets] AS [t]
WHERE [t].[Status] <> CAST(4 AS tinyint) AND [t].[AssignedTo] IS NOT NULL
GROUP BY [t].[AssignedTo]
ORDER BY COUNT(*) DESC, [t].[AssignedTo]', N'@p int', @p = 10;
