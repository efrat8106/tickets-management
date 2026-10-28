using Microsoft.EntityFrameworkCore;
using Tickets.Application.Abstractions;
using Tickets.Application.Tickets.Dtos;
using Tickets.Domain;

namespace Tickets.Application.Tickets;

/// <summary>
/// Computes the ticket summary with GROUP BY queries executed in SQL Server; only the aggregated rows
/// (a handful per query) are transferred, never the tickets themselves.
/// </summary>
public sealed class TicketSummaryService(ITicketsDbContext db, ISummaryCache cache, TimeProvider timeProvider)
{
    public const int TopAssigneesCount = 10;

    /// <summary>Returns the cached summary, computing it on a cache miss.</summary>
    public Task<TicketSummaryDto> GetAsync(CancellationToken cancellationToken) =>
        cache.GetOrCreateAsync(ComputeAsync, cancellationToken);

    private async Task<TicketSummaryDto> ComputeAsync(CancellationToken cancellationToken)
    {
        var tickets = db.Tickets.AsNoTracking();

        var statusCounts = await tickets
            .GroupBy(t => t.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, cancellationToken);

        var priorityCounts = await tickets
            .GroupBy(t => t.Priority)
            .Select(g => new { Priority = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Priority, x => x.Count, cancellationToken);

        var topAssignees = await tickets
            .Where(t => t.Status != TicketStatus.Completed && t.AssignedTo != null)
            .GroupBy(t => t.AssignedTo!)
            // Ordering must be expressed on an anonymous type: EF cannot translate members of a record built via its constructor.
            .Select(g => new { AssignedTo = g.Key, OpenCount = g.Count() })
            .OrderByDescending(x => x.OpenCount)
            .ThenBy(x => x.AssignedTo)
            .Take(TopAssigneesCount)
            .Select(x => new AssigneeOpenCountDto(x.AssignedTo, x.OpenCount))
            .ToListAsync(cancellationToken);

        // Every status/priority is listed, including those with zero tickets, so clients get a stable shape.
        return new TicketSummaryDto(
            TotalCount: statusCounts.Values.Sum(),
            ByStatus: Enum.GetValues<TicketStatus>().Select(s => new StatusCountDto(s, statusCounts.GetValueOrDefault(s))).ToList(),
            ByPriority: Enum.GetValues<TicketPriority>().Select(p => new PriorityCountDto(p, priorityCounts.GetValueOrDefault(p))).ToList(),
            TopAssigneesByOpenTickets: topAssignees,
            GeneratedAt: timeProvider.GetUtcNow().UtcDateTime);
    }
}
