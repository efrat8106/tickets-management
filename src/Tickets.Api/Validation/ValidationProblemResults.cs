using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Tickets.Api.Validation;

/// <summary>
/// Builds the single 400 response shape used for all validation failures – both model-binding errors
/// (e.g. <c>status=Foo</c>) and FluentValidation errors – with camelCase keys matching the names clients send.
/// </summary>
internal static class ValidationProblemResults
{
    private const string DefaultErrorMessage = "The input was not valid.";

    public static BadRequestObjectResult Create(HttpContext httpContext, ModelStateDictionary modelState)
    {
        var normalized = new ModelStateDictionary();
        foreach (var (key, entry) in modelState)
        {
            foreach (var error in entry.Errors)
            {
                normalized.AddModelError(ToCamelCasePath(key),
                    string.IsNullOrEmpty(error.ErrorMessage) ? DefaultErrorMessage : error.ErrorMessage);
            }
        }

        var problem = httpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>()
            .CreateValidationProblemDetails(httpContext, normalized);

        return new BadRequestObjectResult(problem) { ContentTypes = { "application/problem+json" } };
    }

    // "Items[0].RowVersion" -> "items[0].rowVersion"
    private static string ToCamelCasePath(string key) =>
        string.Join('.', key.Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName));
}
