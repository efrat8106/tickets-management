-- Q7: GET /api/tickets/4/history (ticket 4 has history in the deterministic seed). Exact EF SQL.
SET NOCOUNT ON;
SET STATISTICS IO, TIME ON;

EXEC sp_executesql N'SELECT COUNT(*)
FROM [TicketStatusHistory] AS [t]
WHERE [t].[RequestId] = @ticketId', N'@ticketId int', @ticketId = 4;

EXEC sp_executesql N'SELECT [t].[Id], [t].[PreviousStatus], [t].[NewStatus], [t].[ChangedAt], [t].[ChangedBy], [t].[CorrelationId]
FROM [TicketStatusHistory] AS [t]
WHERE [t].[RequestId] = @ticketId
ORDER BY [t].[ChangedAt] DESC, [t].[Id] DESC
OFFSET @p ROWS FETCH NEXT @p1 ROWS ONLY', N'@ticketId int, @p int, @p1 int', @ticketId = 4, @p = 0, @p1 = 20;
