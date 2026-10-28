using Tickets.Application.Tickets.Dtos;
using Tickets.Domain;

namespace Tickets.Application.Tickets;

public enum StatusUpdateOutcome
{
    Updated,
    NotFound,
    InvalidTransition,
    Conflict,
}

/// <summary>
/// Outcome of a status update. Business outcomes are values, not exceptions, so single and bulk updates share
/// the same logic and the API maps each outcome to an HTTP status in one place.
/// </summary>
/// <param name="Ticket">Updated ticket (Updated) or the current state of the ticket (Conflict, InvalidTransition).</param>
public sealed record StatusUpdateResult(
    int TicketId,
    StatusUpdateOutcome Outcome,
    TicketDto? Ticket = null,
    string? Message = null,
    IReadOnlyList<TicketStatus>? AllowedStatuses = null);

/// <summary>Who made the change and which HTTP call it came from – both are written to the audit row.</summary>
public sealed record StatusChangeContext(string ChangedBy, string? CorrelationId);
