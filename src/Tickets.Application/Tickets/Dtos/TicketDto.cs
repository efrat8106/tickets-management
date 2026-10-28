using Tickets.Domain;

namespace Tickets.Application.Tickets.Dtos;

/// <summary>
/// A ticket as returned by the API (list rows, single ticket, update results).
/// <see cref="RowVersion"/> is serialized as Base64 and must be sent back on status updates (single or bulk)
/// for optimistic concurrency.
/// </summary>
public sealed record TicketDto(
    int Id,
    string Title,
    string OrganizationName,
    TicketStatus Status,
    TicketPriority Priority,
    string? AssignedTo,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    byte[] RowVersion);
