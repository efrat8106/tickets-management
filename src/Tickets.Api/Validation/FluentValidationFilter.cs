using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace Tickets.Api.Validation;

/// <summary>
/// Validates every action argument that has a registered <see cref="IValidator{T}"/> and short-circuits with
/// 400 <see cref="ValidationProblemDetails"/> – the same response shape ASP.NET Core uses for model-binding errors,
/// so clients see one consistent validation format.
/// </summary>
public sealed class FluentValidationFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var services = context.HttpContext.RequestServices;

        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null ||
                services.GetService(typeof(IValidator<>).MakeGenericType(argument.GetType())) is not IValidator validator)
            {
                continue;
            }

            var result = await validator.ValidateAsync(new ValidationContext<object>(argument), context.HttpContext.RequestAborted);
            foreach (var error in result.Errors)
            {
                context.ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
            }
        }

        if (!context.ModelState.IsValid)
        {
            var problem = services.GetRequiredService<ProblemDetailsFactory>()
                .CreateValidationProblemDetails(context.HttpContext, context.ModelState);
            context.Result = new BadRequestObjectResult(problem);
            return;
        }

        await next();
    }
}
