using FluentValidation;
using Tickets.Domain;

namespace Tickets.Application.Tickets.Dtos;

/// <param name="NewStatus">Target status.</param>
/// <param name="RowVersion">The RowVersion (Base64) the client last saw for this ticket.</param>
public sealed record UpdateStatusRequest(TicketStatus NewStatus, byte[] RowVersion);

public sealed class UpdateStatusRequestValidator : AbstractValidator<UpdateStatusRequest>
{
    public UpdateStatusRequestValidator()
    {
        RuleFor(r => r.NewStatus).IsInEnum();
        RuleFor(r => r.RowVersion).MustBeValidRowVersion();
    }
}

internal static class RowVersionValidation
{
    /// <summary>SQL Server rowversion is always 8 bytes.</summary>
    public const int Length = 8;

    public static IRuleBuilderOptions<T, byte[]> MustBeValidRowVersion<T>(this IRuleBuilderInitial<T, byte[]> rule) =>
        // Stop after the first failure: the length check must not run on a missing (null) value.
        rule.Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(v => v.Length == Length)
            .WithMessage("'rowVersion' must be the Base64 RowVersion value returned by the API.");
}
