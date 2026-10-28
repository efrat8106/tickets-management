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
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, ServiceLifetime.Scoped);
        return services;
    }
}
