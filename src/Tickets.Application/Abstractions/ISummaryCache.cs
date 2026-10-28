using Tickets.Application.Tickets.Dtos;

namespace Tickets.Application.Abstractions;

/// <summary>
/// Cache for the ticket summary. The in-memory implementation can be replaced by a distributed one (e.g. Redis)
/// for multiple server instances without changing callers.
/// </summary>
public interface ISummaryCache
{
    Task<TicketSummaryDto> GetOrCreateAsync(Func<CancellationToken, Task<TicketSummaryDto>> factory, CancellationToken cancellationToken);

    /// <summary>Removes the cached summary. Call only after a change has been committed to the database.</summary>
    void Invalidate();
}
