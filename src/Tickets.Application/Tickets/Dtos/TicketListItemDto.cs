using Tickets.Domain;

namespace Tickets.Application.Tickets.Dtos;

/// <summary>
/// A row in the ticket list. <see cref="RowVersion"/> is serialized as Base64 and must be sent back on status updates
/// (single or bulk) for optimistic concurrency.
/// </summary>
public sealed record TicketListItemDto(
    int Id,
    string Title,
    string OrganizationName,
    TicketStatus Status,
    TicketPriority Priority,
    string? AssignedTo,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    byte[] RowVersion);
