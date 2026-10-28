using System.Linq.Expressions;
using Tickets.Application.Tickets.Dtos;
using Tickets.Domain;

namespace Tickets.Application.Tickets;

/// <summary>
/// Entity → DTO projections as expressions, so EF translates them into the SELECT list
/// (only the needed columns are read; entities are never materialized or tracked for reads).
/// </summary>
internal static class TicketMappings
{
    public static readonly Expression<Func<Ticket, TicketDto>> ToDto = t => new TicketDto(
        t.Id, t.Title, t.OrganizationName, t.Status, t.Priority, t.AssignedTo, t.CreatedAt, t.UpdatedAt, t.RowVersion);

    public static readonly Expression<Func<TicketStatusHistory, TicketHistoryItemDto>> ToHistoryItem = h => new TicketHistoryItemDto(
        h.Id, h.PreviousStatus, h.NewStatus, h.ChangedAt, h.ChangedBy, h.CorrelationId);
}
