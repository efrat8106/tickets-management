using FluentValidation;
using Tickets.Domain;

namespace Tickets.Application.Tickets.Dtos;

/// <summary>Changes up to <see cref="BulkUpdateStatusRequestValidator.MaxItems"/> tickets to the same status.</summary>
public sealed record BulkUpdateStatusRequest(TicketStatus NewStatus, IReadOnlyList<BulkUpdateItem> Items);

/// <param name="RowVersion">The RowVersion (Base64) the client last saw for this ticket.</param>
public sealed record BulkUpdateItem(int Id, byte[] RowVersion);

/// <summary>
/// Partial success: every item is processed independently and reported with its own outcome, in request order.
/// </summary>
public sealed record BulkUpdateStatusResponse(int Requested, int Succeeded, int Failed, IReadOnlyList<BulkUpdateItemResult> Results);

/// <param name="Ticket">The updated ticket (Updated) or its current state (Conflict / InvalidTransition), so the client
/// can refresh the row and retry with the current RowVersion; null for NotFound.</param>
public sealed record BulkUpdateItemResult(int Id, StatusUpdateOutcome Outcome, TicketDto? Ticket, string? Message);

public sealed class BulkUpdateStatusRequestValidator : AbstractValidator<BulkUpdateStatusRequest>
{
    public const int MaxItems = 100;

    public BulkUpdateStatusRequestValidator()
    {
        RuleFor(r => r.NewStatus).IsInEnum();

        RuleFor(r => r.Items)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(items => items.Count <= MaxItems).WithMessage($"'items' must contain at most {MaxItems} tickets.")
            .Must(items => items.Select(i => i.Id).Distinct().Count() == items.Count).WithMessage("'items' must not contain duplicate ticket ids.");

        RuleForEach(r => r.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.Id).GreaterThan(0);
            item.RuleFor(i => i.RowVersion).MustBeValidRowVersion();
        });
    }
}
