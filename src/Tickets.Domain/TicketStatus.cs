namespace Tickets.Domain;

/// <summary>Stored as tinyint. Values are explicit and must never be renumbered (0 is intentionally unused).</summary>
public enum TicketStatus : byte
{
    New = 1,
    InProgress = 2,
    Waiting = 3,
    Completed = 4,
}
