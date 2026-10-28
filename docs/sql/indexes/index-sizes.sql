-- Size of every index on the two tables (pages are 8 KB). Used to weigh read gains against storage / write cost.
SET NOCOUNT ON;

SELECT OBJECT_NAME(i.object_id) AS [Table],
       i.name AS [Index],
       SUM(ps.used_page_count) AS UsedPages,
       CAST(SUM(ps.used_page_count) * 8 / 1024.0 AS decimal(10, 2)) AS UsedMB
FROM sys.indexes AS i
JOIN sys.dm_db_partition_stats AS ps ON ps.object_id = i.object_id AND ps.index_id = i.index_id
WHERE i.object_id IN (OBJECT_ID('Tickets'), OBJECT_ID('TicketStatusHistory'))
GROUP BY i.object_id, i.name
ORDER BY [Table], [Index];
