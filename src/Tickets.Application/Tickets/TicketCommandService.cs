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
/// <item>After a committed change the cached summary is invalidated.</item>
/// </list>
/// </summary>
public sealed class TicketCommandService(
    ITicketsDbContext db,
    ISummaryCache summaryCache,
    TimeProvider timeProvider,
    ILogger<TicketCommandService> logger)
{
    public async Task<StatusUpdateResult> UpdateStatusAsync(int id, UpdateStatusRequest request, StatusChangeContext context, CancellationToken cancellationToken)
    {
        var ticket = await db.Tickets.SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (ticket is null)
        {
            return StatusUpdateResult.NotFound(id);
        }

        var result = await ApplyStatusChangeAsync(ticket, request.NewStatus, request.RowVersion, context, cancellationToken);

        // Only after a committed change: a failed update leaves the cached summary valid.
        if (result.Outcome == StatusUpdateOutcome.Updated)
        {
            summaryCache.Invalidate();
        }

        return result;
    }

    /// <summary>
    /// Partial success: all tickets are loaded with one query, then each item goes through exactly the same logic as a
    /// single update (<see cref="ApplyStatusChangeAsync"/>) and is saved in its own transaction together with its audit
    /// row. A failing item (not found, invalid transition, conflict) does not affect the others.
    /// Items are processed sequentially because a DbContext is not thread-safe.
    /// </summary>
    public async Task<BulkUpdateStatusResponse> BulkUpdateStatusAsync(BulkUpdateStatusRequest request, StatusChangeContext context, CancellationToken cancellationToken)
    {
        var ids = request.Items.Select(i => i.Id).ToList();
        var tickets = await db.Tickets.Where(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, cancellationToken);

        var results = new List<BulkUpdateItemResult>(request.Items.Count);
        var succeeded = 0;
        try
        {
            foreach (var item in request.Items)
            {
                var result = tickets.TryGetValue(item.Id, out var ticket)
                    ? await ApplyStatusChangeAsync(ticket, request.NewStatus, item.RowVersion, context, cancellationToken)
                    : StatusUpdateResult.NotFound(item.Id);

                results.Add(new BulkUpdateItemResult(item.Id, result.Outcome, result.Ticket, result.Message));
                if (result.Outcome == StatusUpdateOutcome.Updated)
                {
                    succeeded++;
                }
            }
        }
        finally
        {
            // Once per bulk request, if anything was committed – also when the request is cancelled or fails midway,
            // because the items saved before that point are already committed.
            if (succeeded > 0)
            {
                summaryCache.Invalidate();
            }
        }

        logger.LogInformation("Bulk status update to {NewStatus}: {Requested} requested, {Succeeded} succeeded, {Failed} failed",
            request.NewStatus, results.Count, succeeded, results.Count - succeeded);

        return new BulkUpdateStatusResponse(results.Count, succeeded, results.Count - succeeded, results);
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

        // The decisive check: the database compares this original value inside the UPDATE statement itself, so a write
        // committed by someone else after our read is detected (proven by the "write between read and save" test).
        // After the early check it equals the loaded value; setting it explicitly states that the comparison is against
        // the version the client saw.
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
            return current is null ? StatusUpdateResult.NotFound(ticket.Id) : ConflictResult(current);
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
