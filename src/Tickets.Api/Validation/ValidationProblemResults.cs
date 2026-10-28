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
    private const string BodyKey = "body";

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

    // "Items[0].RowVersion" -> "items[0].rowVersion"; JSON paths "$.newStatus" -> "newStatus";
    // header names such as "X-User-Name" are kept as they are.
    private static string ToCamelCasePath(string key)
    {
        if (key.Length == 0)
        {
            return BodyKey; // errors about the request body as a whole (e.g. an empty body)
        }

        if (key.Contains('-'))
        {
            return key;
        }

        var path = key.StartsWith("$.", StringComparison.Ordinal) ? key[2..] : key;
        return string.Join('.', path.Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName));
    }
}
