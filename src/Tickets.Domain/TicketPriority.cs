namespace Tickets.Domain;

/// <summary>Stored as tinyint; numeric order is the business order, so ORDER BY Priority is meaningful.</summary>
public enum TicketPriority : byte
{
    Low = 1,
    Medium = 2,
    High = 3,
}
