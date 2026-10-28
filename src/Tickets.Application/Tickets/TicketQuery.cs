using Tickets.Domain;

namespace Tickets.Application.Tickets;

/// <summary>
/// Query parameters of <c>GET /api/tickets</c>. All filtering, searching, sorting and paging is executed in the database.
/// Multiple values of <see cref="Status"/> / <see cref="Priority"/> are combined with OR; different filters with AND.
/// </summary>
public sealed class TicketQuery
{
    public const int DefaultPageSize = 20;

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = DefaultPageSize;

    public TicketStatus[]? Status { get; init; }
    public TicketPriority[]? Priority { get; init; }

    /// <summary>Exact match (case-insensitive by collation).</summary>
    public string? OrganizationName { get; init; }

    /// <summary>Exact match (case-insensitive by collation).</summary>
    public string? AssignedTo { get; init; }

    /// <summary>Inclusive lower bound on CreatedAt, interpreted as UTC.</summary>
    public DateTime? CreatedFrom { get; init; }

    /// <summary>Inclusive upper bound on CreatedAt, interpreted as UTC.</summary>
    public DateTime? CreatedTo { get; init; }

    /// <summary>Prefix search on Title or OrganizationName.</summary>
    public string? Search { get; init; }

    /// <summary>One of <see cref="TicketSortFields.All"/>; defaults to <see cref="TicketSortFields.Default"/>.</summary>
    public string? SortBy { get; init; }

    /// <summary><c>asc</c> or <c>desc</c>; defaults to <c>desc</c>.</summary>
    public string? SortDir { get; init; }
}
