using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tickets.Application.Abstractions;
using Tickets.Infrastructure.Persistence;

namespace Tickets.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "TicketsDb";

    /// <summary>
    /// Registers persistence services. Lifetimes:
    /// <list type="bullet">
    /// <item><c>TicketsDbContext</c> / <c>ITicketsDbContext</c> – Scoped (one unit of work per HTTP request; DbContext is
    /// not thread-safe). Both resolve to the same instance within a scope.</item>
    /// </list>
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is not configured. " +
                $"Set it in appsettings.Development.json, user-secrets or the ConnectionStrings__{ConnectionStringName} environment variable.");

        services.AddDbContext<TicketsDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<ITicketsDbContext>(sp => sp.GetRequiredService<TicketsDbContext>());

        return services;
    }

    /// <summary>Applies pending EF migrations. Called on startup in Development only.</summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketsDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }
}
