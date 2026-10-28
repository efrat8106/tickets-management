using Serilog.Context;

namespace Tickets.Api.Middleware;

/// <summary>
/// Assigns a correlation id to every request: taken from the <c>X-Correlation-Id</c> header when it is present and
/// well-formed, otherwise the server-generated trace identifier. The id is stored in
/// <see cref="HttpContext.TraceIdentifier"/>, echoed in the response header and pushed into the log context,
/// so a single call can be followed across logs, error responses and audit rows.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";
    private const int MaxLength = 64;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context);
        context.TraceIdentifier = correlationId;

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
        var incoming = context.Request.Headers[HeaderName].ToString();
        return IsWellFormed(incoming) ? incoming : context.TraceIdentifier;
    }

    // Client-supplied values end up in logs and the database, so only short, plain identifiers are accepted.
    private static bool IsWellFormed(string value) =>
        value.Length is > 0 and <= MaxLength &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':');
}
