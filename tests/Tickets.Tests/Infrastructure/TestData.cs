using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tickets.Domain;

namespace Tickets.Tests.Infrastructure;

/// <summary>Builders for known test data and JSON helpers matching the API's serialization settings.</summary>
public static class TestData
{
    public static readonly DateTime BaseTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static Ticket Ticket(
        string title,
        string organization = "Acme",
        TicketStatus status = TicketStatus.New,
        TicketPriority priority = TicketPriority.Medium,
        string? assignedTo = null,
        DateTime? createdAt = null)
    {
        var created = createdAt ?? BaseTime;
        var ticket = new Ticket
        {
            Title = title,
            OrganizationName = organization,
            Priority = priority,
            AssignedTo = assignedTo,
            CreatedAt = created,
            UpdatedAt = created,
        };

        if (status != TicketStatus.New)
        {
            ticket.ChangeStatus(status, "test", correlationId: null, created);
        }

        return ticket;
    }

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(Json))!;
}
