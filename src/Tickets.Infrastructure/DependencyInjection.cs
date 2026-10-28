using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tickets.Infrastructure.Persistence;

namespace Tickets.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "TicketsDb";

    /// <summary>
    /// Registers persistence services. Lifetimes:
    /// <list type="bullet">
    /// <item><c>TicketsDbContext</c> – Scoped (one unit of work per HTTP request; DbContext is not thread-safe).</item>
    /// </list>
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is not configured. " +
                $"Set it in appsettings.Development.json, user-secrets or the ConnectionStrings__{ConnectionStringName} environment variable.");

        services.AddDbContext<TicketsDbContext>(options => options.UseSqlServer(connectionString));

        return services;
    }
}
