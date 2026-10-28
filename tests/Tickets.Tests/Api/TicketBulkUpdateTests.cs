using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tickets.Application.Tickets;
using Tickets.Application.Tickets.Dtos;
using Tickets.Domain;
using Tickets.Tests.Infrastructure;
using static Tickets.Tests.Infrastructure.TestData;

namespace Tickets.Tests.Api;

/// <summary>Bulk status update: partial success with a per-item outcome.</summary>
[Collection(ApiCollection.Name)]
public sealed class TicketBulkUpdateTests(ApiFixture fixture) : IAsyncLifetime
{
    private static readonly byte[] StaleRowVersion = [0, 0, 0, 0, 0, 0, 0, 1];

    private readonly HttpClient _client = fixture.CreateClient();

    public Task InitializeAsync() => fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Mixed_request_reports_each_outcome_in_order_and_persists_only_the_valid_items()
    {
        var validA = Ticket("valid A", status: TicketStatus.InProgress);
        var validB = Ticket("valid B", status: TicketStatus.New);
        var completed = Ticket("completed", status: TicketStatus.Completed);
        var stale = Ticket("stale", status: TicketStatus.InProgress);
        await AddAsync(validA, validB, completed, stale);

        var response = await _client.PostAsJsonAsync("/api/tickets/bulk-status", new BulkUpdateStatusRequest(TicketStatus.InProgress,
        [
            new(validA.Id, validA.RowVersion), // InProgress -> InProgress: same status, invalid
            new(validB.Id, validB.RowVersion), // New -> InProgress: valid
            new(999_999, StaleRowVersion),     // does not exist
            new(completed.Id, completed.RowVersion), // Completed -> InProgress (reopen): valid
            new(stale.Id, StaleRowVersion),    // outdated RowVersion: conflict
        ]), Json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsync<BulkUpdateStatusResponse>();

        body.Requested.Should().Be(5);
        body.Succeeded.Should().Be(2);
        body.Failed.Should().Be(3);
        body.Results.Select(r => (r.Id, r.Outcome)).Should().Equal(
            (validA.Id, StatusUpdateOutcome.InvalidTransition),
            (validB.Id, StatusUpdateOutcome.Updated),
            (999_999, StatusUpdateOutcome.NotFound),
            (completed.Id, StatusUpdateOutcome.Updated),
            (stale.Id, StatusUpdateOutcome.Conflict));

        // Updated items carry the new RowVersion; failed items carry the current state for refresh.
        body.Results[1].Ticket!.RowVersion.Should().NotEqual(validB.RowVersion);
        body.Results[4].Ticket!.RowVersion.Should().Equal(stale.RowVersion);
        body.Results[2].Ticket.Should().BeNull();

        (await StatusesAsync()).Should().BeEquivalentTo(new Dictionary<int, TicketStatus>
        {
            [validA.Id] = TicketStatus.InProgress,
            [validB.Id] = TicketStatus.InProgress,
            [completed.Id] = TicketStatus.InProgress,
            [stale.Id] = TicketStatus.InProgress,
        });
        (await AuditTicketIdsAsync()).Should().BeEquivalentTo([validB.Id, completed.Id], "only successful items are audited");
    }

    [Fact]
    public async Task Database_level_conflict_on_one_item_does_not_affect_the_following_items()
    {
        // The first item loses a real read/write race (detected by the database, not by the early check).
        // The failed entity must be detached; otherwise the next item's SaveChanges would re-send it and fail too.
        var first = Ticket("first", status: TicketStatus.InProgress);
        var second = Ticket("second", status: TicketStatus.InProgress);
        await AddAsync(first, second);

        var service = new ConcurrentWriterInterceptor(fixture.ConnectionString, first.Id, TicketStatus.Completed).CreateService(out var db);
        await using var _ = db;

        var result = await service.BulkUpdateStatusAsync(
            new BulkUpdateStatusRequest(TicketStatus.Waiting, [new(first.Id, first.RowVersion), new(second.Id, second.RowVersion)]),
            new StatusChangeContext("dana", null), CancellationToken.None);

        result.Results.Select(r => r.Outcome).Should().Equal(StatusUpdateOutcome.Conflict, StatusUpdateOutcome.Updated);
        (await StatusesAsync()).Should().BeEquivalentTo(new Dictionary<int, TicketStatus>
        {
            [first.Id] = TicketStatus.Completed, // the other writer's change survives
            [second.Id] = TicketStatus.Waiting,
        });
        (await AuditTicketIdsAsync()).Should().Equal(second.Id);
    }

    [Fact]
    public async Task Exactly_100_items_are_accepted()
    {
        var items = Enumerable.Range(1, 100).Select(id => new BulkUpdateItem(id, StaleRowVersion)).ToList();

        var response = await _client.PostAsJsonAsync("/api/tickets/bulk-status", new BulkUpdateStatusRequest(TicketStatus.InProgress, items), Json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.ReadAsync<BulkUpdateStatusResponse>()).Results.Should().HaveCount(100)
            .And.OnlyContain(r => r.Outcome == StatusUpdateOutcome.NotFound);
    }

    public static TheoryData<string, string> InvalidRequests => new()
    {
        { $$"""{"newStatus":"InProgress","items":[{{string.Join(",", Enumerable.Range(1, 101).Select(i => $$"""{"id":{{i}},"rowVersion":"AAAAAAAAAAE="}"""))}}]}""", "items" },
        { """{"newStatus":"InProgress","items":[{"id":1,"rowVersion":"AAAAAAAAAAE="},{"id":1,"rowVersion":"AAAAAAAAAAE="}]}""", "items" },
        { """{"newStatus":"InProgress","items":[]}""", "items" },
        { """{"newStatus":"InProgress"}""", "items" },
        { """{"newStatus":"InProgress","items":[{"id":1,"rowVersion":"AAAA"}]}""", "items[0].rowVersion" },
        { """{"newStatus":"Nope","items":[{"id":1,"rowVersion":"AAAAAAAAAAE="}]}""", "newStatus" },
    };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task Invalid_request_returns_400_and_changes_nothing(string body, string expectedKey)
    {
        var ticket = Ticket("untouched", status: TicketStatus.New);
        await AddAsync(ticket);

        var response = await _client.PostAsync("/api/tickets/bulk-status", new StringContent(body, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("errors").EnumerateObject().Select(p => p.Name).Should().Contain(expectedKey);
        (await AuditTicketIdsAsync()).Should().BeEmpty();
    }

    private Task AddAsync(params Ticket[] tickets) =>
        fixture.WithDbContextAsync(async db =>
        {
            db.Tickets.AddRange(tickets);
            await db.SaveChangesAsync();
        });

    private Task<Dictionary<int, TicketStatus>> StatusesAsync() =>
        fixture.WithDbContextAsync(db => db.Tickets.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Status));

    private Task<List<int>> AuditTicketIdsAsync() =>
        fixture.WithDbContextAsync(db => db.TicketStatusHistory.AsNoTracking().OrderBy(h => h.Id).Select(h => h.RequestId).ToListAsync());
}
