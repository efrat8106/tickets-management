using FluentValidation;

namespace Tickets.Application.Common;

/// <summary>Plain paging parameters for secondary lists (e.g. ticket history).</summary>
public sealed class PageRequest
{
    public const int MaxPageSize = 100;

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;

    public int Skip => (Page - 1) * PageSize;
}

public sealed class PageRequestValidator : AbstractValidator<PageRequest>
{
    public PageRequestValidator()
    {
        RuleFor(p => p.Page).GreaterThanOrEqualTo(1);
        RuleFor(p => p.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize);
    }
}
