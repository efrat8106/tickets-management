namespace Tickets.Application.Tickets;

/// <summary>Whitelist of sortable fields and directions for the ticket list (names as used in the query string).</summary>
public static class TicketSortFields
{
    public const string CreatedAt = "createdAt";
    public const string UpdatedAt = "updatedAt";
    public const string Priority = "priority";
    public const string Status = "status";
    public const string Title = "title";
    public const string OrganizationName = "organizationName";

    public const string Default = CreatedAt;

    public const string Ascending = "asc";
    public const string Descending = "desc";

    public static readonly IReadOnlyList<string> All = [CreatedAt, UpdatedAt, Priority, Status, Title, OrganizationName];

    public static bool IsValidField(string value) => All.Contains(value, StringComparer.OrdinalIgnoreCase);

    public static bool IsValidDirection(string value) =>
        value.Equals(Ascending, StringComparison.OrdinalIgnoreCase) || value.Equals(Descending, StringComparison.OrdinalIgnoreCase);
}
