using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Tickets.Application.Abstractions;
using Tickets.Application.Common;
using Tickets.Application.Tickets.Dtos;
using Tickets.Domain;

namespace Tickets.Application.Tickets;

/// <summary>
/// Read side of tickets. Every query is composed as <see cref="IQueryable{T}"/> and executed in SQL Server:
/// filters become WHERE, sorting ORDER BY, paging OFFSET/FETCH, and only the DTO columns are selected.
/// Nothing is loaded into memory before paging.
/// </summary>
public sealed class TicketQueryService(ITicketsDbContext db)
{
    /// <summary>Returns the ticket, or null if it does not exist.</summary>
    public Task<TicketDto?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        db.Tickets.AsNoTracking()
            .Where(t => t.Id == id)
            .Select(TicketMappings.ToDto)
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>Returns the status history of a ticket (newest first), or null if the ticket does not exist.</summary>
    public async Task<PagedResult<TicketHistoryItemDto>?> GetHistoryAsync(int ticketId, PageRequest paging, CancellationToken cancellationToken)
    {
        if (!await db.Tickets.AnyAsync(t => t.Id == ticketId, cancellationToken))
        {
            return null;
        }

        var history = db.TicketStatusHistory.AsNoTracking().Where(h => h.RequestId == ticketId);
        var totalCount = await history.CountAsync(cancellationToken);

        var items = paging.Skip >= totalCount
            ? []
            : await history
                .OrderByDescending(h => h.ChangedAt)
                .ThenByDescending(h => h.Id)
                .Skip(paging.Skip)
                .Take(paging.PageSize)
                .Select(TicketMappings.ToHistoryItem)
                .ToListAsync(cancellationToken);

        return new PagedResult<TicketHistoryItemDto>(items, paging.Page, paging.PageSize, totalCount);
    }

    public async Task<PagedResult<TicketDto>> GetTicketsAsync(TicketQuery query, CancellationToken cancellationToken)
    {
        var filtered = ApplyFilters(db.Tickets.AsNoTracking(), query);

        // Separate COUNT query over the same filters (no ORDER BY / paging). Its cost is measured in PERFORMANCE.md.
        var totalCount = await filtered.CountAsync(cancellationToken);

        var skip = (query.Page - 1) * query.PageSize;
        if (skip >= totalCount)
        {
            return new PagedResult<TicketDto>([], query.Page, query.PageSize, totalCount);
        }

        var items = await ApplySorting(filtered, query.SortBy, query.SortDir)
            .Skip(skip)
            .Take(query.PageSize)
            .Select(TicketMappings.ToDto)
            .ToListAsync(cancellationToken);

        return new PagedResult<TicketDto>(items, query.Page, query.PageSize, totalCount);
    }

    private static IQueryable<Ticket> ApplyFilters(IQueryable<Ticket> tickets, TicketQuery query)
    {
        if (query.Status is { Length: > 0 } statuses)
        {
            tickets = tickets.Where(t => statuses.Contains(t.Status));
        }

        if (query.Priority is { Length: > 0 } priorities)
        {
            tickets = tickets.Where(t => priorities.Contains(t.Priority));
        }

        if (!string.IsNullOrWhiteSpace(query.OrganizationName))
        {
            var organization = query.OrganizationName.Trim();
            tickets = tickets.Where(t => t.OrganizationName == organization);
        }

        if (!string.IsNullOrWhiteSpace(query.AssignedTo))
        {
            var assignee = query.AssignedTo.Trim();
            tickets = tickets.Where(t => t.AssignedTo == assignee);
        }

        if (query.CreatedFrom is { } from)
        {
            var fromUtc = ToUtc(from);
            tickets = tickets.Where(t => t.CreatedAt >= fromUtc);
        }

        if (query.CreatedTo is { } to)
        {
            var toUtc = ToUtc(to);
            tickets = tickets.Where(t => t.CreatedAt <= toUtc);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // Prefix search (LIKE 'term%') can use an index seek; a "contains" search ('%term%') would scan the table.
            var term = query.Search.Trim();
            tickets = tickets.Where(t => t.Title.StartsWith(term) || t.OrganizationName.StartsWith(term));
        }

        return tickets;
    }

    private static IQueryable<Ticket> ApplySorting(IQueryable<Ticket> tickets, string? sortBy, string? sortDir)
    {
        var descending = !string.Equals(sortDir, TicketSortFields.Ascending, StringComparison.OrdinalIgnoreCase);

        var ordered = (sortBy ?? TicketSortFields.Default).ToLowerInvariant() switch
        {
            "updatedat" => OrderBy(tickets, t => t.UpdatedAt, descending),
            "priority" => OrderBy(tickets, t => t.Priority, descending),
            "status" => OrderBy(tickets, t => t.Status, descending),
            "title" => OrderBy(tickets, t => t.Title, descending),
            "organizationname" => OrderBy(tickets, t => t.OrganizationName, descending),
            _ => OrderBy(tickets, t => t.CreatedAt, descending),
        };

        // Id as tie-breaker makes the order total, so paging is deterministic (no rows repeated or skipped across pages).
        return descending ? ordered.ThenByDescending(t => t.Id) : ordered.ThenBy(t => t.Id);
    }

    private static IOrderedQueryable<Ticket> OrderBy<TKey>(IQueryable<Ticket> tickets, Expression<Func<Ticket, TKey>> key, bool descending) =>
        descending ? tickets.OrderByDescending(key) : tickets.OrderBy(key);

    /// <summary>
    /// Query-string dates arrive as Unspecified (no offset → treated as UTC) or Local (ASP.NET converts "...Z" values to
    /// server local time). Both are normalized to UTC, matching how CreatedAt is stored.
    /// </summary>
    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Local => value.ToUniversalTime(),
        DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        _ => value,
    };
}
