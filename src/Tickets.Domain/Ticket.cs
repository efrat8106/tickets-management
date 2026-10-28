namespace Tickets.Domain;

public class Ticket
{
    public const int TitleMaxLength = 200;
    public const int OrganizationNameMaxLength = 200;
    public const int AssignedToMaxLength = 100;

    public int Id { get; set; }
    public required string Title { get; set; }
    public required string OrganizationName { get; set; }
    public TicketStatus Status { get; private set; } = TicketStatus.New;
    public TicketPriority Priority { get; set; }
    public string? AssignedTo { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];

    /// <summary>
    /// Changes the status and returns the matching audit entry. Status, UpdatedAt and the audit row always change
    /// together; the caller is responsible for validating the transition and saving both in one SaveChanges.
    /// </summary>
    public TicketStatusHistory ChangeStatus(TicketStatus newStatus, string changedBy, string? correlationId, DateTime utcNow)
    {
        var history = new TicketStatusHistory
        {
            RequestId = Id,
            PreviousStatus = Status,
            NewStatus = newStatus,
            ChangedAt = utcNow,
            ChangedBy = changedBy,
            CorrelationId = correlationId,
        };

        Status = newStatus;
        UpdatedAt = utcNow;

        return history;
    }
}
