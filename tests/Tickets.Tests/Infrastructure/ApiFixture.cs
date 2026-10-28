using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;
using Tickets.Application.Abstractions;
using Tickets.Infrastructure;
using Tickets.Infrastructure.Persistence;

namespace Tickets.Tests.Infrastructure;

/// <summary>
/// Runs the real API in memory against a dedicated SQL Server container (Testcontainers).
/// Isolation from the development database:
/// <list type="bullet">
/// <item>The container is started by the tests, on a random host port, without a persistent volume.</item>
/// <item>The environment is "Testing", so appsettings.Development.json (dev connection string) is never loaded.</item>
/// <item><see cref="EnsureIsolatedDatabase"/> refuses to run if the API is connected to anything else.</item>
/// </list>
/// One container is shared by all test classes in <see cref="ApiCollection"/>; tests in a collection run sequentially,
/// and each test resets the data it depends on with <see cref="ResetDatabaseAsync"/>.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    public const string DatabaseName = "TicketsTests";

    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
    private WebApplicationFactory<Program> _factory = null!;

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        ConnectionString = new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = DatabaseName,
        }.ConnectionString;

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:TicketsDb", ConnectionString);
            builder.ConfigureTestServices(services =>
                services.AddControllers().AddApplicationPart(typeof(ApiFixture).Assembly));
        });

        EnsureIsolatedDatabase();
        await _factory.Services.MigrateDatabaseAsync();
    }

    public HttpClient CreateClient() => _factory.CreateClient();

    /// <summary>Runs an action with a fresh DbContext scope (arrange / assert directly against the database).</summary>
    public async Task<T> WithDbContextAsync<T>(Func<TicketsDbContext, Task<T>> action)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TicketsDbContext>());
    }

    public Task WithDbContextAsync(Func<TicketsDbContext, Task> action) =>
        WithDbContextAsync(async db => { await action(db); return true; });

    /// <summary>
    /// Deletes all data in the test database and clears the summary cache, so no test sees a summary cached by a
    /// previous one. Never touches the development database (see class docs).
    /// </summary>
    public async Task ResetDatabaseAsync()
    {
        await WithDbContextAsync(db => db.Database.ExecuteSqlRawAsync("DELETE FROM TicketStatusHistory; DELETE FROM Tickets;"));
        _factory.Services.GetRequiredService<ISummaryCache>().Invalidate();
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _container.DisposeAsync();
    }

    private void EnsureIsolatedDatabase()
    {
        using var scope = _factory.Services.CreateScope();
        var actual = new SqlConnectionStringBuilder(
            scope.ServiceProvider.GetRequiredService<TicketsDbContext>().Database.GetConnectionString());
        var expected = new SqlConnectionStringBuilder(ConnectionString);

        if (actual.DataSource != expected.DataSource || actual.InitialCatalog != DatabaseName)
        {
            throw new InvalidOperationException(
                $"Refusing to run tests: the API is connected to '{actual.DataSource}/{actual.InitialCatalog}' " +
                $"instead of the Testcontainers database '{expected.DataSource}/{DatabaseName}'.");
        }
    }
}
