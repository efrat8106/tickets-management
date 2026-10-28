using Tickets.Domain;

namespace Tickets.Application.Tickets;

/// <summary>
/// The single source of truth for allowed status changes. Any pair not listed – including a change to the same
/// status – is rejected.
/// <code>
/// New        → InProgress
/// InProgress → Waiting, Completed
/// Waiting    → InProgress, Completed
/// Completed  → InProgress   (reopen)
/// </code>
/// </summary>
public static class StatusTransitions
{
    private static readonly IReadOnlyDictionary<TicketStatus, TicketStatus[]> Allowed = new Dictionary<TicketStatus, TicketStatus[]>
    {
        [TicketStatus.New] = [TicketStatus.InProgress],
        [TicketStatus.InProgress] = [TicketStatus.Waiting, TicketStatus.Completed],
        [TicketStatus.Waiting] = [TicketStatus.InProgress, TicketStatus.Completed],
        [TicketStatus.Completed] = [TicketStatus.InProgress],
    };

    public static bool IsAllowed(TicketStatus from, TicketStatus to) =>
        Allowed.TryGetValue(from, out var targets) && targets.Contains(to);

    public static IReadOnlyList<TicketStatus> AllowedFrom(TicketStatus from) =>
        Allowed.TryGetValue(from, out var targets) ? targets : [];
}
