using System.Net;
using System.Text.Json;
using FluentAssertions;
using Tickets.Application.Common;
using Tickets.Application.Tickets.Dtos;
using Tickets.Domain;
using Tickets.Tests.Infrastructure;
using static Tickets.Tests.Infrastructure.TestData;

namespace Tickets.Tests.Api;

/// <summary>Integration tests for the read endpoints, against SQL Server with small, known data sets.</summary>
[Collection(ApiCollection.Name)]
public sealed class TicketQueryTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly HttpClient _client = fixture.CreateClient();

    public Task InitializeAsync() => fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Combined_filters_sorting_and_paging_return_the_right_page_and_total()
    {
        await AddAsync(
            Ticket("A", status: TicketStatus.New, priority: TicketPriority.High, createdAt: BaseTime.AddDays(1)),
            Ticket("B", status: TicketStatus.Waiting, priority: TicketPriority.High, createdAt: BaseTime.AddDays(2)),
            Ticket("C", status: TicketStatus.New, priority: TicketPriority.High, createdAt: BaseTime.AddDays(3)),
            Ticket("D", status: TicketStatus.Waiting, priority: TicketPriority.High, createdAt: BaseTime.AddDays(4)),
            Ticket("E", status: TicketStatus.New, priority: TicketPriority.Low, createdAt: BaseTime.AddDays(5)),        // wrong priority
            Ticket("F", status: TicketStatus.Completed, priority: TicketPriority.High, createdAt: BaseTime.AddDays(6)), // wrong status
            Ticket("G", status: TicketStatus.InProgress, priority: TicketPriority.High, createdAt: BaseTime.AddDays(7))); // wrong status

        var result = await GetPageAsync("?status=New&status=Waiting&priority=High&sortBy=createdAt&sortDir=asc&page=2&pageSize=3");

        result.TotalCount.Should().Be(4);
        result.TotalPages.Should().Be(2);
        result.Page.Should().Be(2);
        result.Items.Select(t => t.Title).Should().Equal("D");
    }

    [Fact]
    public async Task Assignee_and_inclusive_utc_date_range_filters_are_combined()
    {
        await AddAsync(
            Ticket("before", assignedTo: "dana", createdAt: BaseTime.AddDays(-1)),
            Ticket("from-boundary", assignedTo: "dana", createdAt: BaseTime),
            Ticket("to-boundary", assignedTo: "dana", createdAt: BaseTime.AddDays(10)),
            Ticket("after", assignedTo: "dana", createdAt: BaseTime.AddDays(10).AddSeconds(1)),
            Ticket("other-assignee", assignedTo: "yossi", createdAt: BaseTime.AddDays(5)));

        var result = await GetPageAsync("?assignedTo=dana&createdFrom=2026-01-01T00:00:00Z&createdTo=2026-01-11T00:00:00Z&sortBy=createdAt&sortDir=asc");

        result.Items.Select(t => t.Title).Should().Equal("from-boundary", "to-boundary");
    }

    [Fact]
    public async Task Search_is_a_prefix_match_on_title_or_organization()
    {
        await AddAsync(
            Ticket("Invoice missing", organization: "Cedar"),
            Ticket("Access request", organization: "Invoice Corp"),
            Ticket("Re: Invoice", organization: "Cedar"),
            Ticket("בקשת החזר", organization: "Cedar"));

        (await GetPageAsync("?search=invoice&sortBy=title&sortDir=asc")).Items.Select(t => t.Title)
            .Should().Equal("Access request", "Invoice missing");

        (await GetPageAsync("?search=" + Uri.EscapeDataString("בקשת"))).Items.Select(t => t.Title)
            .Should().Equal("בקשת החזר");
    }

    [Theory]
    [InlineData("50%", "50% off")]
    [InlineData("5_", "5_0 units")]
    [InlineData("[a]", "[a] bracket")]
    public async Task Search_treats_like_wildcards_literally(string search, string expectedTitle)
    {
        await AddAsync(
            Ticket("50% off"),
            Ticket("500 units"),
            Ticket("5_0 units"),
            Ticket("[a] bracket"),
            Ticket("a bracket"));

        var result = await GetPageAsync("?search=" + Uri.EscapeDataString(search));

        result.Items.Select(t => t.Title).Should().Equal(expectedTitle);
    }

    [Fact]
    public async Task Page_beyond_the_end_returns_empty_items_with_correct_totals()
    {
        await AddAsync(Enumerable.Range(1, 5).Select(i => Ticket($"T{i}")).ToArray());

        var result = await GetPageAsync("?page=10&pageSize=2");

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(5);
        result.TotalPages.Should().Be(3);
        result.Page.Should().Be(10);
    }

    [Theory]
    [InlineData("asc")]
    [InlineData("desc")]
    public async Task Paging_through_equal_sort_keys_returns_every_ticket_exactly_once(string sortDir)
    {
        // All tickets share the same sort key, so only the Id tie-breaker makes the order deterministic.
        var tickets = Enumerable.Range(1, 25)
            .Select(i => Ticket($"T{i}", priority: TicketPriority.High, createdAt: BaseTime))
            .ToArray();
        await AddAsync(tickets);
        var allIds = tickets.Select(t => t.Id).ToList();

        var seen = new List<int>();
        for (var page = 1; page <= 4; page++)
        {
            var result = await GetPageAsync($"?sortBy=priority&sortDir={sortDir}&page={page}&pageSize=7");
            seen.AddRange(result.Items.Select(t => t.Id));
        }

        seen.Should().OnlyHaveUniqueItems("no ticket may appear on two pages");
        seen.Should().BeEquivalentTo(allIds, "no ticket may be skipped");
        seen.Should().Equal(sortDir == "asc" ? allIds.Order() : allIds.OrderDescending(), "ties are broken by Id in the requested direction");
    }

    [Fact]
    public async Task Invalid_query_returns_400_validation_problem_with_camel_case_keys()
    {
        var response = await _client.GetAsync("/api/tickets?pageSize=1000&sortBy=id&createdFrom=2026-02-01&createdTo=2026-01-01");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var errors = await ReadErrorsAsync(response);
        errors.Keys.Should().BeEquivalentTo("pageSize", "sortBy", "createdTo");
        errors["pageSize"].Single().Should().Contain("between 1 and 100");
    }

    [Theory]
    [InlineData("?status=Foo", "status")]
    [InlineData("?page=0", "page")]
    [InlineData("?sortDir=up", "sortDir")]
    public async Task Each_invalid_parameter_is_reported_under_its_query_name(string query, string expectedKey)
    {
        var response = await _client.GetAsync("/api/tickets" + query);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorsAsync(response)).Keys.Should().Equal(expectedKey);
    }

    [Fact]
    public async Task Single_ticket_and_history_are_returned_and_unknown_ticket_is_404()
    {
        var ticket = Ticket("With history", status: TicketStatus.Completed);
        await AddAsync(ticket);
        await fixture.WithDbContextAsync(async db =>
        {
            db.TicketStatusHistory.AddRange(
                new TicketStatusHistory { RequestId = ticket.Id, PreviousStatus = TicketStatus.New, NewStatus = TicketStatus.InProgress, ChangedAt = BaseTime.AddHours(1), ChangedBy = "dana" },
                new TicketStatusHistory { RequestId = ticket.Id, PreviousStatus = TicketStatus.InProgress, NewStatus = TicketStatus.Completed, ChangedAt = BaseTime.AddHours(2), ChangedBy = "dana" });
            await db.SaveChangesAsync();
        });

        var details = await (await _client.GetAsync($"/api/tickets/{ticket.Id}")).ReadAsync<TicketDto>();
        details.Title.Should().Be("With history");
        details.Status.Should().Be(TicketStatus.Completed);
        details.RowVersion.Should().NotBeEmpty();

        var history = await (await _client.GetAsync($"/api/tickets/{ticket.Id}/history")).ReadAsync<PagedResult<TicketHistoryItemDto>>();
        history.TotalCount.Should().Be(2);
        history.Items.Select(h => h.NewStatus).Should().Equal(TicketStatus.Completed, TicketStatus.InProgress); // newest first

        (await _client.GetAsync("/api/tickets/999999")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _client.GetAsync("/api/tickets/999999/history")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Summary_aggregates_counts_by_status_priority_and_open_tickets_per_assignee()
    {
        await AddAsync(
            Ticket("1", status: TicketStatus.New, priority: TicketPriority.High, assignedTo: "dana"),
            Ticket("2", status: TicketStatus.InProgress, priority: TicketPriority.High, assignedTo: "dana"),
            Ticket("3", status: TicketStatus.Completed, priority: TicketPriority.Low, assignedTo: "dana"), // completed: not open
            Ticket("4", status: TicketStatus.Waiting, priority: TicketPriority.Low, assignedTo: "yossi"),
            Ticket("5", status: TicketStatus.New, priority: TicketPriority.Low));                           // unassigned

        var summary = await (await _client.GetAsync("/api/tickets/summary")).ReadAsync<TicketSummaryDto>();

        summary.TotalCount.Should().Be(5);
        summary.ByStatus.Should().Equal(
            new StatusCountDto(TicketStatus.New, 2), new StatusCountDto(TicketStatus.InProgress, 1),
            new StatusCountDto(TicketStatus.Waiting, 1), new StatusCountDto(TicketStatus.Completed, 1));
        summary.ByPriority.Should().Equal(
            new PriorityCountDto(TicketPriority.Low, 3), new PriorityCountDto(TicketPriority.Medium, 0),
            new PriorityCountDto(TicketPriority.High, 2));
        summary.TopAssigneesByOpenTickets.Should().Equal(
            new AssigneeOpenCountDto("dana", 2), new AssigneeOpenCountDto("yossi", 1));
    }

    private Task AddAsync(params Ticket[] tickets) =>
        fixture.WithDbContextAsync(async db =>
        {
            db.Tickets.AddRange(tickets);
            await db.SaveChangesAsync();
        });

    private async Task<PagedResult<TicketDto>> GetPageAsync(string query)
    {
        var response = await _client.GetAsync("/api/tickets" + query);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<PagedResult<TicketDto>>();
    }

    private static async Task<Dictionary<string, string[]>> ReadErrorsAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("errors").EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.EnumerateArray().Select(v => v.GetString()!).ToArray());
    }
}
