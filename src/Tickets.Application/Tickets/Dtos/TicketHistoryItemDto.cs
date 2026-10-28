using Tickets.Domain;

namespace Tickets.Application.Tickets.Dtos;

public sealed record TicketHistoryItemDto(
    long Id,
    TicketStatus PreviousStatus,
    TicketStatus NewStatus,
    DateTime ChangedAt,
    string ChangedBy,
    string? CorrelationId);
