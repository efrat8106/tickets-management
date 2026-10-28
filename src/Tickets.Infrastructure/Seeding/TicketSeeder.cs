using System.Data;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Tickets.Domain;
using Tickets.Infrastructure.Persistence;

namespace Tickets.Infrastructure.Seeding;

/// <summary>
/// Loads generated test data with <see cref="SqlBulkCopy"/> (not EF SaveChanges in a loop). The whole operation runs
/// in one transaction: either the full data set is loaded or nothing changes.
/// Ids are generated explicitly (KeepIdentity), so the same count always produces exactly the same rows and ids.
/// </summary>
public sealed class TicketSeeder(TicketsDbContext db, ILogger<TicketSeeder> logger)
{
    public const int RandomSeed = 20261028;
    private const int BatchSize = 10_000;

    public async Task SeedAsync(SeedOptions options, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(db.Database.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var existing = await ExecuteScalarAsync<int>(connection, transaction, "SELECT COUNT(*) FROM Tickets", cancellationToken);
        if (existing > 0 && !options.Reset)
        {
            logger.LogWarning("Database already contains {Existing} tickets; nothing was inserted. Use --reset to recreate the data", existing);
            return;
        }

        var stopwatch = Stopwatch.StartNew();

        if (options.Reset)
        {
            await ExecuteAsync(connection, transaction, "TRUNCATE TABLE TicketStatusHistory; DELETE FROM Tickets;", cancellationToken);
            logger.LogInformation("Removed {Existing} existing tickets", existing);
        }

        var (tickets, history) = new SeedDataGenerator(RandomSeed).Generate(options.Count);
        for (var i = 0; i < history.Count; i++)
        {
            history[i].Id = i + 1;
        }

        await BulkInsertAsync(connection, transaction, "Tickets", ToDataTable(tickets), cancellationToken);
        await BulkInsertAsync(connection, transaction, "TicketStatusHistory", ToDataTable(history), cancellationToken);

        // Align identity counters with the explicitly inserted ids, so new rows continue after them.
        await ExecuteAsync(connection, transaction, """
            DECLARE @maxTicket int = (SELECT ISNULL(MAX(Id), 0) FROM Tickets);
            DBCC CHECKIDENT ('Tickets', RESEED, @maxTicket) WITH NO_INFOMSGS;
            DECLARE @maxHistory bigint = (SELECT ISNULL(MAX(Id), 0) FROM TicketStatusHistory);
            DBCC CHECKIDENT ('TicketStatusHistory', RESEED, @maxHistory) WITH NO_INFOMSGS;
            """, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation("Seeded {TicketCount} tickets and {HistoryCount} history rows in {ElapsedMs} ms",
            tickets.Count, history.Count, stopwatch.ElapsedMilliseconds);
    }

    private static async Task BulkInsertAsync(SqlConnection connection, SqlTransaction transaction, string table, DataTable data, CancellationToken cancellationToken)
    {
        // CheckConstraints: validate CHECK and FK constraints during the load and keep them trusted by the optimizer.
        using var bulkCopy = new SqlBulkCopy(connection, SqlBulkCopyOptions.KeepIdentity | SqlBulkCopyOptions.TableLock | SqlBulkCopyOptions.CheckConstraints, transaction)
        {
            DestinationTableName = table,
            BatchSize = BatchSize,
            BulkCopyTimeout = 300,
        };

        foreach (DataColumn column in data.Columns)
        {
            bulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
        }

        await bulkCopy.WriteToServerAsync(data, cancellationToken);
    }

    // RowVersion is not mapped: SQL Server generates it.
    private static DataTable ToDataTable(List<Ticket> tickets)
    {
        var table = new DataTable();
        table.Columns.Add(nameof(Ticket.Id), typeof(int));
        table.Columns.Add(nameof(Ticket.Title), typeof(string));
        table.Columns.Add(nameof(Ticket.OrganizationName), typeof(string));
        table.Columns.Add(nameof(Ticket.Status), typeof(byte));
        table.Columns.Add(nameof(Ticket.Priority), typeof(byte));
        table.Columns.Add(nameof(Ticket.AssignedTo), typeof(string));
        table.Columns.Add(nameof(Ticket.CreatedAt), typeof(DateTime));
        table.Columns.Add(nameof(Ticket.UpdatedAt), typeof(DateTime));

        foreach (var t in tickets)
        {
            table.Rows.Add(t.Id, t.Title, t.OrganizationName, (byte)t.Status, (byte)t.Priority,
                (object?)t.AssignedTo ?? DBNull.Value, t.CreatedAt, t.UpdatedAt);
        }

        return table;
    }

    private static DataTable ToDataTable(List<TicketStatusHistory> history)
    {
        var table = new DataTable();
        table.Columns.Add(nameof(TicketStatusHistory.Id), typeof(long));
        table.Columns.Add(nameof(TicketStatusHistory.RequestId), typeof(int));
        table.Columns.Add(nameof(TicketStatusHistory.PreviousStatus), typeof(byte));
        table.Columns.Add(nameof(TicketStatusHistory.NewStatus), typeof(byte));
        table.Columns.Add(nameof(TicketStatusHistory.ChangedAt), typeof(DateTime));
        table.Columns.Add(nameof(TicketStatusHistory.ChangedBy), typeof(string));

        foreach (var h in history)
        {
            table.Rows.Add(h.Id, h.RequestId, (byte)h.PreviousStatus, (byte)h.NewStatus, h.ChangedAt, h.ChangedBy);
        }

        return table;
    }

    private static async Task ExecuteAsync(SqlConnection connection, SqlTransaction transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<T> ExecuteScalarAsync<T>(SqlConnection connection, SqlTransaction transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        return (T)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}
