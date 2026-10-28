using Tickets.Domain;

namespace Tickets.Application.Tickets.Dtos;

/// <summary>Aggregated ticket statistics, computed in the database.</summary>
/// <param name="GeneratedAt">When the numbers were computed (relevant because the summary is cached).</param>
public sealed record TicketSummaryDto(
    int TotalCount,
    IReadOnlyList<StatusCountDto> ByStatus,
    IReadOnlyList<PriorityCountDto> ByPriority,
    IReadOnlyList<AssigneeOpenCountDto> TopAssigneesByOpenTickets,
    DateTime GeneratedAt);

public sealed record StatusCountDto(TicketStatus Status, int Count);

public sealed record PriorityCountDto(TicketPriority Priority, int Count);

/// <param name="OpenCount">Tickets assigned to this person whose status is not Completed.</param>
public sealed record AssigneeOpenCountDto(string AssignedTo, int OpenCount);
