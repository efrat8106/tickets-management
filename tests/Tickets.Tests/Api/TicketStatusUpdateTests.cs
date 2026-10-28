using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Tickets.Api.Controllers;
using Tickets.Application.Tickets;
using Tickets.Application.Tickets.Dtos;
using Tickets.Domain;
using Tickets.Infrastructure.Persistence;
using Tickets.Tests.Infrastructure;
using static Tickets.Tests.Infrastructure.TestData;

namespace Tickets.Tests.Api;

/// <summary>Status update, optimistic concurrency and audit – against SQL Server with real rowversion semantics.</summary>
[Collection(ApiCollection.Name)]
public sealed class TicketStatusUpdateTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly HttpClient _client = fixture.CreateClient();

    public Task InitializeAsync() => fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Valid_update_returns_new_row_version_and_writes_one_audit_row()
    {
        var ticket = await AddTicketAsync(TicketStatus.New);

        using var request = PutStatus(ticket.Id, TicketStatus.InProgress, ticket.RowVersion);
        request.Headers.Add(TicketsController.UserNameHeader, "dana.cohen");
        request.Headers.Add("X-Correlation-Id", "corr-1");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.ReadAsync<TicketDto>();
        updated.Status.Should().Be(TicketStatus.InProgress);
        updated.RowVersion.Should().NotEqual(ticket.RowVersion);
        updated.UpdatedAt.Should().BeAfter(ticket.UpdatedAt);

        var audit = await AuditRowsAsync(ticket.Id);
        audit.Should().ContainSingle();
        audit[0].Should().BeEquivalentTo(new
        {
            RequestId = ticket.Id,
            PreviousStatus = TicketStatus.New,
            NewStatus = TicketStatus.InProgress,
            ChangedBy = "dana.cohen",
            CorrelationId = "corr-1",
            ChangedAt = updated.UpdatedAt,
        });
    }

    [Fact]
    public async Task Second_update_with_the_same_row_version_gets_409_and_no_second_audit_row()
    {
        var ticket = await AddTicketAsync(TicketStatus.InProgress);

        var first = await _client.SendAsync(PutStatus(ticket.Id, TicketStatus.Waiting, ticket.RowVersion));
        var second = await _client.SendAsync(PutStatus(ticket.Id, TicketStatus.Completed, ticket.RowVersion));

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        second.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        // The 409 carries the current state so the client can refresh without another request.
        var current = await ReadExtensionAsync<TicketDto>(second, "currentTicket");
        current.Status.Should().Be(TicketStatus.Waiting);
        current.RowVersion.Should().Equal((await first.ReadAsync<TicketDto>()).RowVersion);

        (await AuditRowsAsync(ticket.Id)).Should().ContainSingle().Which.NewStatus.Should().Be(TicketStatus.Waiting);
        (await CurrentStatusAsync(ticket.Id)).Should().Be(TicketStatus.Waiting, "the second update must not overwrite the first");
    }

    [Fact]
    public async Task Parallel_updates_with_the_same_row_version_produce_exactly_one_success()
    {
        const int parallelRequests = 10;
        var ticket = await AddTicketAsync(TicketStatus.New);

        var responses = await Task.WhenAll(Enumerable.Range(0, parallelRequests)
            .Select(_ => _client.SendAsync(PutStatus(ticket.Id, TicketStatus.InProgress, ticket.RowVersion))));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(parallelRequests - 1);
        (await AuditRowsAsync(ticket.Id)).Should().ContainSingle();
    }

    [Fact]
    public async Task Write_between_our_read_and_our_save_is_rejected_by_the_database()
    {
        // Deterministic race: another writer commits after the service has loaded the ticket and passed the early
        // RowVersion check, right before SaveChanges. Only the database-level check (UPDATE … WHERE RowVersion = @original)
        // can detect this – it proves there is no lost update.
        var ticket = await AddTicketAsync(TicketStatus.InProgress);

        var options = new DbContextOptionsBuilder<TicketsDbContext>()
            .UseSqlServer(fixture.ConnectionString)
            .AddInterceptors(new ConcurrentWriterInterceptor(fixture.ConnectionString, ticket.Id, TicketStatus.Completed))
            .Options;
        await using var db = new TicketsDbContext(options);
        var service = new TicketCommandService(db, TimeProvider.System, NullLogger<TicketCommandService>.Instance);

        var result = await service.UpdateStatusAsync(ticket.Id, new UpdateStatusRequest(TicketStatus.Waiting, ticket.RowVersion),
            new StatusChangeContext("dana", null), CancellationToken.None);

        result.Outcome.Should().Be(StatusUpdateOutcome.Conflict);
        result.Ticket!.Status.Should().Be(TicketStatus.Completed, "the conflict response reports the other writer's state");
        (await CurrentStatusAsync(ticket.Id)).Should().Be(TicketStatus.Completed, "the other writer's change must survive");
        (await AuditRowsAsync(ticket.Id)).Should().BeEmpty("the failed update must not leave an audit row");
    }

    [Theory]
    [InlineData(TicketStatus.InProgress, TicketStatus.New)]        // forbidden
    [InlineData(TicketStatus.InProgress, TicketStatus.InProgress)] // same status
    [InlineData(TicketStatus.New, TicketStatus.Completed)]         // skipping a step
    public async Task Disallowed_transition_returns_422_with_allowed_statuses_and_changes_nothing(TicketStatus from, TicketStatus to)
    {
        var ticket = await AddTicketAsync(from);

        var response = await _client.SendAsync(PutStatus(ticket.Id, to, ticket.RowVersion));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var allowed = await ReadExtensionAsync<TicketStatus[]>(response, "allowedStatuses");
        allowed.Should().Equal(StatusTransitions.AllowedFrom(from));

        (await CurrentStatusAsync(ticket.Id)).Should().Be(from);
        (await AuditRowsAsync(ticket.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task Unknown_ticket_returns_404()
    {
        var response = await _client.SendAsync(PutStatus(999_999, TicketStatus.InProgress, new byte[8]));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("{}", "newStatus", "rowVersion")]                              // regression: used to return 500
    [InlineData("""{"newStatus":"InProgress","rowVersion":"AAAA"}""", "rowVersion")] // not 8 bytes
    [InlineData("""{"newStatus":"Foo","rowVersion":"AAAAAAAAAAA="}""", "newStatus")]
    public async Task Malformed_body_returns_400_for_the_offending_fields(string body, params string[] expectedKeys)
    {
        var ticket = await AddTicketAsync(TicketStatus.New);

        var response = await _client.PutAsync($"/api/tickets/{ticket.Id}/status",
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("errors").EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(expectedKeys);
    }

    private async Task<Ticket> AddTicketAsync(TicketStatus status)
    {
        var ticket = Ticket("Concurrency test", status: status);
        await fixture.WithDbContextAsync(async db =>
        {
            db.Tickets.Add(ticket);
            await db.SaveChangesAsync();
        });
        return ticket;
    }

    private static HttpRequestMessage PutStatus(int id, TicketStatus newStatus, byte[] rowVersion) =>
        new(HttpMethod.Put, $"/api/tickets/{id}/status")
        {
            Content = JsonContent.Create(new UpdateStatusRequest(newStatus, rowVersion), options: Json),
        };

    private Task<List<TicketStatusHistory>> AuditRowsAsync(int ticketId) =>
        fixture.WithDbContextAsync(db => db.TicketStatusHistory.AsNoTracking().Where(h => h.RequestId == ticketId).ToListAsync());

    private Task<TicketStatus> CurrentStatusAsync(int ticketId) =>
        fixture.WithDbContextAsync(db => db.Tickets.AsNoTracking().Where(t => t.Id == ticketId).Select(t => t.Status).SingleAsync());

    private static async Task<T> ReadExtensionAsync<T>(HttpResponseMessage response, string name)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty(name).Deserialize<T>(Json)!;
    }

    /// <summary>Simulates another user committing a change to the same row just before our SaveChanges.</summary>
    private sealed class ConcurrentWriterInterceptor(string connectionString, int ticketId, TicketStatus otherWritersStatus) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand("UPDATE Tickets SET Status = @status, UpdatedAt = SYSUTCDATETIME() WHERE Id = @id", connection);
            command.Parameters.AddWithValue("@status", (byte)otherWritersStatus);
            command.Parameters.AddWithValue("@id", ticketId);
            await command.ExecuteNonQueryAsync(cancellationToken);

            return result;
        }
    }
}
