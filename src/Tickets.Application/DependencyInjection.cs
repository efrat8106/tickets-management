using System.Text.Json;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Tickets.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers application services. Lifetimes:
    /// <list type="bullet">
    /// <item>Validators – Scoped (FluentValidation's default lifetime for assembly scanning).</item>
    /// </list>
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Validation error keys and messages use the camelCase names clients send (pageSize, not PageSize).
        ValidatorOptions.Global.PropertyNameResolver = (_, member, _) => ToCamelCase(member?.Name);
        ValidatorOptions.Global.DisplayNameResolver = (_, member, _) => ToCamelCase(member?.Name);

        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, ServiceLifetime.Scoped);
        return services;
    }

    private static string? ToCamelCase(string? name) => name is null ? null : JsonNamingPolicy.CamelCase.ConvertName(name);
}
