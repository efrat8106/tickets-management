namespace Tickets.Domain;

/// <summary>Audit entry for a single status change of a ticket.</summary>
public class TicketStatusHistory
{
    public const int ChangedByMaxLength = 100;
    public const int CorrelationIdMaxLength = 64;

    public long Id { get; set; }

    /// <summary>FK to <see cref="Ticket.Id"/> (the ticket is called "request" in the specification).</summary>
    public int RequestId { get; set; }

    public TicketStatus PreviousStatus { get; set; }
    public TicketStatus NewStatus { get; set; }
    public DateTime ChangedAt { get; set; }
    public required string ChangedBy { get; set; }

    /// <summary>HTTP correlation id of the call that made the change, for tracing back to logs.</summary>
    public string? CorrelationId { get; set; }
}
