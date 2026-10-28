using System.Text.Json;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Tickets.Application.Tickets;

namespace Tickets.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers application services. Lifetimes:
    /// <list type="bullet">
    /// <item>Validators – Scoped (FluentValidation's default lifetime for assembly scanning).</item>
    /// <item>Ticket services – Scoped (they depend on the Scoped DbContext).</item>
    /// </list>
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Messages are always English (not the server's OS culture) and name fields as clients send them ('pageSize').
        ValidatorOptions.Global.LanguageManager.Enabled = false;
        ValidatorOptions.Global.DisplayNameResolver = (_, member, _) => ToCamelCase(member?.Name);

        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, ServiceLifetime.Scoped);
        services.AddScoped<TicketQueryService>();
        return services;
    }

    private static string? ToCamelCase(string? name) => name is null ? null : JsonNamingPolicy.CamelCase.ConvertName(name);
}
