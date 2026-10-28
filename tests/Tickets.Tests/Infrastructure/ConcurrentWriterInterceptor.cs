using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Tickets.Application.Tickets;
using Tickets.Domain;
using Tickets.Infrastructure.Persistence;

namespace Tickets.Tests.Infrastructure;

/// <summary>
/// Simulates another user committing a change to a ticket from a separate connection right before the first
/// SaveChanges of the intercepted DbContext – i.e. after the service has read the ticket, before it writes.
/// This creates a deterministic read/write race that only the database-level RowVersion check can detect.
/// </summary>
public sealed class ConcurrentWriterInterceptor(string connectionString, int ticketId, TicketStatus otherWritersStatus) : SaveChangesInterceptor
{
    private bool _fired;

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (_fired)
        {
            return result;
        }

        _fired = true;
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("UPDATE Tickets SET Status = @status, UpdatedAt = SYSUTCDATETIME() WHERE Id = @id", connection);
        command.Parameters.AddWithValue("@status", (byte)otherWritersStatus);
        command.Parameters.AddWithValue("@id", ticketId);
        await command.ExecuteNonQueryAsync(cancellationToken);

        return result;
    }

    /// <summary>Creates a command service whose DbContext is intercepted by this writer.</summary>
    public TicketCommandService CreateService(out TicketsDbContext db)
    {
        db = new TicketsDbContext(new DbContextOptionsBuilder<TicketsDbContext>()
            .UseSqlServer(connectionString)
            .AddInterceptors(this)
            .Options);
        return new TicketCommandService(db, TimeProvider.System, NullLogger<TicketCommandService>.Instance);
    }
}
