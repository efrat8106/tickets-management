using FluentAssertions;
using FluentValidation.TestHelper;
using Tickets.Application.Tickets;
using Tickets.Domain;

namespace Tickets.Tests.Application;

public sealed class TicketQueryValidatorTests
{
    private readonly TicketQueryValidator _validator = new();

    [Fact]
    public void Default_query_is_valid() =>
        _validator.TestValidate(new TicketQuery()).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Fully_specified_valid_query_is_valid() =>
        _validator.TestValidate(new TicketQuery
        {
            Page = 3,
            PageSize = 100,
            Status = [TicketStatus.New, TicketStatus.Waiting],
            Priority = [TicketPriority.High],
            OrganizationName = "Acme Logistics",
            AssignedTo = "dana.cohen",
            CreatedFrom = new DateTime(2026, 1, 1),
            CreatedTo = new DateTime(2026, 1, 1),
            Search = "Invoice",
            SortBy = "OrganizationName",
            SortDir = "ASC",
        }).ShouldNotHaveAnyValidationErrors();

    [Theory]
    [InlineData(0, 20)]
    [InlineData(-1, 20)]
    public void Page_below_one_is_invalid(int page, int pageSize) =>
        _validator.TestValidate(new TicketQuery { Page = page, PageSize = pageSize })
            .ShouldHaveValidationErrorFor(q => q.Page);

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    [InlineData(1000)]
    public void Page_size_outside_1_to_100_is_invalid(int pageSize) =>
        _validator.TestValidate(new TicketQuery { PageSize = pageSize })
            .ShouldHaveValidationErrorFor(q => q.PageSize);

    [Fact]
    public void Undefined_enum_values_are_invalid()
    {
        var result = _validator.TestValidate(new TicketQuery
        {
            Status = [(TicketStatus)7],
            Priority = [(TicketPriority)0],
        });

        // Key casing (camelCase) is applied by the API layer and verified by the API integration tests.
        result.Errors.Should().Contain(e => e.PropertyName.Equals("status[0]", StringComparison.OrdinalIgnoreCase));
        result.Errors.Should().Contain(e => e.PropertyName.Equals("priority[0]", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Reversed_date_range_is_invalid() =>
        _validator.TestValidate(new TicketQuery
        {
            CreatedFrom = new DateTime(2026, 2, 1),
            CreatedTo = new DateTime(2026, 1, 1),
        }).ShouldHaveValidationErrorFor(q => q.CreatedTo);

    [Theory]
    [InlineData("id")]
    [InlineData("assignedTo")]
    [InlineData("createdAt; DROP TABLE Tickets")]
    public void Sort_field_outside_whitelist_is_invalid(string sortBy) =>
        _validator.TestValidate(new TicketQuery { SortBy = sortBy })
            .ShouldHaveValidationErrorFor(q => q.SortBy);

    [Fact]
    public void Unknown_sort_direction_is_invalid() =>
        _validator.TestValidate(new TicketQuery { SortDir = "up" })
            .ShouldHaveValidationErrorFor(q => q.SortDir);

    [Fact]
    public void Search_longer_than_limit_is_invalid() =>
        _validator.TestValidate(new TicketQuery { Search = new string('a', TicketQueryValidator.SearchMaxLength + 1) })
            .ShouldHaveValidationErrorFor(q => q.Search);
}
