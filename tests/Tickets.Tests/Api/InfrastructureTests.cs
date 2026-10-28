using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Tickets.Api.Middleware;
using Tickets.Tests.Infrastructure;

namespace Tickets.Tests.Api;

[Collection(ApiCollection.Name)]
public sealed class InfrastructureTests(ApiFixture fixture)
{
    [Fact]
    public async Task Tests_run_against_the_isolated_testcontainers_database()
    {
        await fixture.ResetDatabaseAsync();

        var (database, ticketCount) = await fixture.WithDbContextAsync(async db =>
            (new SqlConnectionStringBuilder(db.Database.GetConnectionString()).InitialCatalog,
             await db.Tickets.CountAsync()));

        database.Should().Be(ApiFixture.DatabaseName);
        ticketCount.Should().Be(0);
    }

    [Fact]
    public async Task Health_endpoint_reports_healthy_when_database_is_reachable()
    {
        var response = await fixture.CreateClient().GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }

    [Fact]
    public async Task Unhandled_exception_returns_problem_details_without_internal_details()
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Add(CorrelationIdMiddleware.HeaderName, "test-500");

        var response = await client.GetAsync("/__tests/throw");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Should().ContainSingle().Which.Should().Be("test-500");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(ThrowingTestController.SecretMessage, "exception details must not leak outside Development");

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Status.Should().Be(500);
        problem.Detail.Should().BeNull();
        problem.Extensions["correlationId"]!.ToString().Should().Be("test-500");
    }
}
