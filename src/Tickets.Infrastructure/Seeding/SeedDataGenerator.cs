using Tickets.Domain;

namespace Tickets.Infrastructure.Seeding;

/// <summary>
/// Generates deterministic, realistic test data: the same seed and count always produce the same rows.
/// Distributions are intentionally skewed (most tickets completed, a few large organizations, some unassigned
/// tickets) so filters and indexes are exercised on non-uniform data, like in production.
/// </summary>
internal sealed class SeedDataGenerator(int randomSeed)
{
    /// <summary>Fixed end of the generated date range, so output does not depend on when the seed runs.</summary>
    public static readonly DateTime RangeEnd = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private const int RangeDays = 730;
    private const string SystemUser = "system";

    private static readonly string[] Organizations = BuildOrganizations();

    private static readonly string[] Assignees =
    [
        "dana.cohen", "yossi.levi", "noa.mizrahi", "amit.peretz", "shira.biton", "eitan.friedman", "maya.avraham",
        "omer.katz", "tamar.shapiro", "ron.azoulay", "lior.dahan", "michal.goldberg", "itay.ben-david", "yael.ohana",
        "gil.rosen", "hila.malka", "nadav.weiss", "roni.amar", "avi.halevi", "keren.segal",
    ];

    private static readonly string[] Topics =
    [
        "Invoice discrepancy", "Access request", "Payroll question", "Contract renewal", "Data export",
        "Password reset", "Billing address change", "Report error", "Integration failure", "New employee onboarding",
        "Tax certificate", "Refund request", "Service outage", "Permission update", "Document upload issue",
        "בקשת החזר", "עדכון פרטי ארגון", "שאלה בנושא שכר", "תקלה במערכת", "בקשה לאישור מסמכים",
    ];

    private static readonly string[] Details =
    [
        "urgent", "follow-up", "Q1", "Q2", "Q3", "Q4", "branch office", "headquarters", "second request", "new",
    ];

    private readonly Random _random = new(randomSeed);

    public (List<Ticket> Tickets, List<TicketStatusHistory> History) Generate(int count)
    {
        var tickets = new List<Ticket>(count);
        var history = new List<TicketStatusHistory>();

        for (var id = 1; id <= count; id++)
        {
            var ticket = CreateTicket(id);
            tickets.Add(ticket);

            // Roughly every tenth processed ticket gets an audit trail consistent with its current status.
            if (ticket.Status != TicketStatus.New && _random.Next(10) == 0)
            {
                history.AddRange(CreateHistory(ticket));
            }
        }

        return (tickets, history);
    }

    private Ticket CreateTicket(int id)
    {
        var createdAt = RangeEnd.AddMinutes(-_random.Next(RangeDays * 24 * 60));
        var ageDays = (RangeEnd - createdAt).TotalDays;
        var status = PickStatus(ageDays);

        var updatedAt = status == TicketStatus.New
            ? createdAt
            : Min(createdAt.AddMinutes(_random.Next(1, 30 * 24 * 60)), RangeEnd);

        var ticket = new Ticket
        {
            Id = id,
            Title = $"{Topics[_random.Next(Topics.Length)]} - {Details[_random.Next(Details.Length)]}",
            OrganizationName = Organizations[Skewed(Organizations.Length)],
            Priority = PickPriority(),
            AssignedTo = PickAssignee(status),
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
        };

        if (status != TicketStatus.New)
        {
            // Sets the status without producing an audit entry; history is generated separately for a subset.
            ticket.ChangeStatus(status, SystemUser, correlationId: null, updatedAt);
        }

        return ticket;
    }

    /// <summary>Older tickets are much more likely to be completed.</summary>
    private TicketStatus PickStatus(double ageDays)
    {
        var roll = _random.Next(100);
        return ageDays > 180
            ? roll switch { < 85 => TicketStatus.Completed, < 92 => TicketStatus.Waiting, < 98 => TicketStatus.InProgress, _ => TicketStatus.New }
            : roll switch { < 30 => TicketStatus.New, < 60 => TicketStatus.InProgress, < 75 => TicketStatus.Waiting, _ => TicketStatus.Completed };
    }

    private TicketPriority PickPriority() => _random.Next(100) switch
    {
        < 30 => TicketPriority.Low,
        < 80 => TicketPriority.Medium,
        _ => TicketPriority.High,
    };

    /// <summary>New tickets are often unassigned; a few processed tickets are unassigned too.</summary>
    private string? PickAssignee(TicketStatus status)
    {
        var unassignedChance = status == TicketStatus.New ? 50 : 5;
        return _random.Next(100) < unassignedChance ? null : Assignees[Skewed(Assignees.Length)];
    }

    private IEnumerable<TicketStatusHistory> CreateHistory(Ticket ticket)
    {
        TicketStatus[] path = ticket.Status switch
        {
            TicketStatus.InProgress => [TicketStatus.New, TicketStatus.InProgress],
            TicketStatus.Waiting => [TicketStatus.New, TicketStatus.InProgress, TicketStatus.Waiting],
            TicketStatus.Completed => [TicketStatus.New, TicketStatus.InProgress, TicketStatus.Completed],
            _ => [],
        };

        var steps = path.Length - 1;
        var span = ticket.UpdatedAt - ticket.CreatedAt;

        for (var i = 1; i <= steps; i++)
        {
            yield return new TicketStatusHistory
            {
                RequestId = ticket.Id,
                PreviousStatus = path[i - 1],
                NewStatus = path[i],
                // The last change happens exactly at UpdatedAt; earlier ones are spread evenly before it.
                ChangedAt = ticket.CreatedAt + span * i / steps,
                ChangedBy = ticket.AssignedTo ?? SystemUser,
            };
        }
    }

    /// <summary>Index in [0, length) biased toward low values (a few very common, many rare).</summary>
    private int Skewed(int length) => (int)(Math.Pow(_random.NextDouble(), 2) * length);

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

    private static string[] BuildOrganizations()
    {
        string[] englishBases = ["Acme", "Blue River", "Cedar", "Delta", "Evergreen"];
        string[] englishSuffixes = ["Logistics", "Health", "Systems", "Foods", "Construction"];
        string[] hebrewBases = ["אורן", "גלבוע", "כרמל", "נגב", "שקד"];
        string[] hebrewSuffixes = ["תעשיות", "שירותים", "טכנולוגיות", "מזון", "בנייה"];

        return englishBases.SelectMany(b => englishSuffixes.Select(s => $"{b} {s}"))
            .Concat(hebrewBases.SelectMany(b => hebrewSuffixes.Select(s => $"{b} {s}")))
            .ToArray();
    }
}
