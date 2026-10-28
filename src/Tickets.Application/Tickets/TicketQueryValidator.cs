using FluentValidation;
using Tickets.Domain;

namespace Tickets.Application.Tickets;

public sealed class TicketQueryValidator : AbstractValidator<TicketQuery>
{
    public const int MaxPageSize = 100;
    public const int SearchMaxLength = 100;

    public TicketQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, MaxPageSize);

        // Model binding accepts numeric values for enums (e.g. status=7), so undefined values are rejected here.
        RuleForEach(q => q.Status).IsInEnum();
        RuleForEach(q => q.Priority).IsInEnum();

        RuleFor(q => q.OrganizationName).MaximumLength(Ticket.OrganizationNameMaxLength);
        RuleFor(q => q.AssignedTo).MaximumLength(Ticket.AssignedToMaxLength);
        RuleFor(q => q.Search).MaximumLength(SearchMaxLength);

        RuleFor(q => q.CreatedTo)
            .Must((query, createdTo) => query.CreatedFrom is null || createdTo is null || query.CreatedFrom <= createdTo)
            .WithMessage("'createdTo' must be on or after 'createdFrom'.");

        RuleFor(q => q.SortBy!)
            .Must(TicketSortFields.IsValidField)
            .When(q => q.SortBy is not null)
            .WithMessage($"'sortBy' must be one of: {string.Join(", ", TicketSortFields.All)}.");

        RuleFor(q => q.SortDir!)
            .Must(TicketSortFields.IsValidDirection)
            .When(q => q.SortDir is not null)
            .WithMessage($"'sortDir' must be '{TicketSortFields.Ascending}' or '{TicketSortFields.Descending}'.");
    }
}
