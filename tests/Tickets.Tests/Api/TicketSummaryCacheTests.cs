using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Tickets.Application.Tickets.Dtos;
using Tickets.Domain;
using Tickets.Tests.Infrastructure;
using static Tickets.Tests.Infrastructure.TestData;

namespace Tickets.Tests.Api;

/// <summary>
/// Summary cache behavior end to end. Each test first proves the cache is active (a row inserted directly into the
/// database, bypassing the API, is not visible), then checks what does and does not invalidate it.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class TicketSummaryCacheTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly HttpClient _client = fixture.CreateClient();

    public Task InitializeAsync() => fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Committed_status_update_invalidates_the_cached_summary()
    {
        var ticket = await AddAsync(Ticket("tracked", status: TicketStatus.New));
        (await GetSummaryAsync()).TotalCount.Should().Be(1);

        await AddAsync(Ticket("inserted behind the API's back"));
        (await GetSummaryAsync()).TotalCount.Should().Be(1, "the summary is served from the cache");

        var update = await _client.PutAsJsonAsync($"/api/tickets/{ticket.Id}/status",
            new UpdateStatusRequest(TicketStatus.InProgress, ticket.RowVersion), Json);
        update.StatusCode.Should().Be(HttpStatusCode.OK);

        var summary = await GetSummaryAsync();
        summary.TotalCount.Should().Be(2, "the update invalidated the cache, so the summary was recomputed");
        Count(summary, TicketStatus.InProgress).Should().Be(1);
        Count(summary, TicketStatus.New).Should().Be(1);
    }

    [Fact]
    public async Task Failed_status_update_does_not_invalidate_the_cached_summary()
    {
        var ticket = await AddAsync(Ticket("tracked", status: TicketStatus.New));
        await GetSummaryAsync();
        await AddAsync(Ticket("inserted behind the API's back"));

        var conflict = await _client.PutAsJsonAsync($"/api/tickets/{ticket.Id}/status",
            new UpdateStatusRequest(TicketStatus.InProgress, [0, 0, 0, 0, 0, 0, 0, 1]), Json);
        var invalidTransition = await _client.PutAsJsonAsync($"/api/tickets/{ticket.Id}/status",
            new UpdateStatusRequest(TicketStatus.Completed, ticket.RowVersion), Json);

        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        invalidTransition.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await GetSummaryAsync()).TotalCount.Should().Be(1, "nothing was committed, so the cached summary is still valid");
    }

    [Fact]
    public async Task Bulk_update_with_at_least_one_success_invalidates_the_cached_summary()
    {
        var ticket = await AddAsync(Ticket("tracked", status: TicketStatus.New));
        await GetSummaryAsync();
        await AddAsync(Ticket("inserted behind the API's back"));

        var bulk = await _client.PostAsJsonAsync("/api/tickets/bulk-status", new BulkUpdateStatusRequest(TicketStatus.InProgress,
            [new(ticket.Id, ticket.RowVersion), new(999_999, [0, 0, 0, 0, 0, 0, 0, 1])]), Json);
        bulk.StatusCode.Should().Be(HttpStatusCode.OK);

        var summary = await GetSummaryAsync();
        summary.TotalCount.Should().Be(2);
        Count(summary, TicketStatus.InProgress).Should().Be(1);
    }

    private async Task<Ticket> AddAsync(Ticket ticket)
    {
        await fixture.WithDbContextAsync(async db =>
        {
            db.Tickets.Add(ticket);
            await db.SaveChangesAsync();
        });
        return ticket;
    }

    private async Task<TicketSummaryDto> GetSummaryAsync() =>
        await (await _client.GetAsync("/api/tickets/summary")).ReadAsync<TicketSummaryDto>();

    private static int Count(TicketSummaryDto summary, TicketStatus status) =>
        summary.ByStatus.Single(s => s.Status == status).Count;
}
