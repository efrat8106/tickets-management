using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Tickets.Application.Abstractions;
using Tickets.Application.Tickets.Dtos;
using Tickets.Domain;

namespace Tickets.Application.Tickets;

/// <summary>
/// Status changes with optimistic concurrency enforced by the database:
/// <list type="number">
/// <item>The client sends the RowVersion it saw; it is set as the entity's <b>original</b> RowVersion.</item>
/// <item>EF issues <c>UPDATE … WHERE Id = @id AND RowVersion = @clientRowVersion</c>. If anyone changed the row in
/// the meantime, 0 rows are affected and EF throws <see cref="DbUpdateConcurrencyException"/> → Conflict.</item>
/// <item>The ticket update and its audit row are saved in one <c>SaveChanges</c> (one transaction): if the update
/// fails, no audit row is written.</item>
/// </list>
/// </summary>
public sealed class TicketCommandService(ITicketsDbContext db, TimeProvider timeProvider, ILogger<TicketCommandService> logger)
{
    public async Task<StatusUpdateResult> UpdateStatusAsync(int id, UpdateStatusRequest request, StatusChangeContext context, CancellationToken cancellationToken)
    {
        var ticket = await db.Tickets.SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (ticket is null)
        {
            return new StatusUpdateResult(id, StatusUpdateOutcome.NotFound, Message: $"Ticket {id} does not exist.");
        }

        return await ApplyStatusChangeAsync(ticket, request.NewStatus, request.RowVersion, context, cancellationToken);
    }

    private async Task<StatusUpdateResult> ApplyStatusChangeAsync(
        Ticket ticket, TicketStatus newStatus, byte[] clientRowVersion, StatusChangeContext context, CancellationToken cancellationToken)
    {
        // Early, cheap detection: the client's copy is already outdated. Checked before the transition rule, so a
        // client that saw an old status gets "reload" (409) rather than a misleading transition error.
        if (!ticket.RowVersion.AsSpan().SequenceEqual(clientRowVersion))
        {
            return ConflictResult(ticket);
        }

        if (!StatusTransitions.IsAllowed(ticket.Status, newStatus))
        {
            var allowed = StatusTransitions.AllowedFrom(ticket.Status);
            return new StatusUpdateResult(ticket.Id, StatusUpdateOutcome.InvalidTransition, ToDto(ticket),
                $"Cannot change status from {ticket.Status} to {newStatus}. Allowed: {(allowed.Count == 0 ? "none" : string.Join(", ", allowed))}.",
                allowed);
        }

        // The decisive check: the database compares this original value inside the UPDATE statement itself.
        db.Tickets.Entry(ticket).Property(t => t.RowVersion).OriginalValue = clientRowVersion;

        var previousStatus = ticket.Status;
        var history = ticket.ChangeStatus(newStatus, context.ChangedBy, context.CorrelationId, UtcNowInStoragePrecision());
        db.TicketStatusHistory.Add(history);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Lost the race between our read and our write. Discard local changes so the context stays usable (bulk).
            db.Tickets.Entry(ticket).State = EntityState.Detached;
            db.TicketStatusHistory.Entry(history).State = EntityState.Detached;

            var current = await db.Tickets.AsNoTracking().SingleOrDefaultAsync(t => t.Id == ticket.Id, cancellationToken);
            return current is null
                ? new StatusUpdateResult(ticket.Id, StatusUpdateOutcome.NotFound, Message: $"Ticket {ticket.Id} does not exist.")
                : ConflictResult(current);
        }

        logger.LogInformation("Ticket {TicketId} status changed from {PreviousStatus} to {NewStatus} by {ChangedBy}",
            ticket.Id, previousStatus, newStatus, context.ChangedBy);

        return new StatusUpdateResult(ticket.Id, StatusUpdateOutcome.Updated, ToDto(ticket));
    }

    private StatusUpdateResult ConflictResult(Ticket current)
    {
        logger.LogWarning("Concurrency conflict on ticket {TicketId}: the client's RowVersion is outdated", current.Id);
        return new StatusUpdateResult(current.Id, StatusUpdateOutcome.Conflict, ToDto(current),
            "The ticket was modified by another user. Reload it and try again.");
    }

    private static TicketDto ToDto(Ticket ticket) => TicketMappings.ToDtoCompiled(ticket);

    /// <summary>
    /// Timestamps are stored as datetime2(3); truncating here keeps the value returned right after an update identical
    /// to what a later read returns.
    /// </summary>
    private DateTime UtcNowInStoragePrecision()
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMillisecond));
    }
}
