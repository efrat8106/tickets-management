using Microsoft.AspNetCore.Mvc;
using Tickets.Api.Validation;
using Tickets.Application.Common;
using Tickets.Application.Tickets;
using Tickets.Application.Tickets.Dtos;
using Tickets.Domain;

namespace Tickets.Api.Controllers;

[ApiController]
[Route("api/tickets")]
public sealed class TicketsController(
    TicketQueryService queries,
    TicketSummaryService summary,
    TicketCommandService commands) : ControllerBase
{
    public const string UserNameHeader = "X-User-Name";
    private const string DefaultUserName = "system";

    /// <summary>Returns a page of tickets, filtered, searched and sorted in the database.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<TicketDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<PagedResult<TicketDto>> GetTickets([FromQuery] TicketQuery query, CancellationToken cancellationToken) =>
        queries.GetTicketsAsync(query, cancellationToken);

    /// <summary>Aggregated statistics: counts by status and priority, top assignees by open tickets.</summary>
    [HttpGet("summary")]
    [ProducesResponseType<TicketSummaryDto>(StatusCodes.Status200OK)]
    public Task<TicketSummaryDto> GetSummary(CancellationToken cancellationToken) =>
        summary.ComputeAsync(cancellationToken);

    /// <summary>Returns a single ticket, including its current RowVersion.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType<TicketDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TicketDto>> GetTicket(int id, CancellationToken cancellationToken)
    {
        var ticket = await queries.GetByIdAsync(id, cancellationToken);
        return ticket is null ? TicketNotFound(id) : ticket;
    }

    /// <summary>Returns the status change history of a ticket, newest first.</summary>
    [HttpGet("{id:int}/history")]
    [ProducesResponseType<PagedResult<TicketHistoryItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResult<TicketHistoryItemDto>>> GetHistory(int id, [FromQuery] PageRequest paging, CancellationToken cancellationToken)
    {
        var history = await queries.GetHistoryAsync(id, paging, cancellationToken);
        return history is null ? TicketNotFound(id) : history;
    }

    /// <summary>
    /// Changes the status of a ticket. The body must contain the RowVersion the client last saw;
    /// if the ticket was changed since, the response is 409 with the current ticket.
    /// </summary>
    /// <param name="userName">Optional <c>X-User-Name</c> header, stored as ChangedBy in the audit (default "system").</param>
    [HttpPut("{id:int}/status")]
    [ProducesResponseType<TicketDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<TicketDto>> UpdateStatus(
        int id,
        UpdateStatusRequest request,
        [FromHeader(Name = UserNameHeader)] string? userName,
        CancellationToken cancellationToken)
    {
        if (!TryCreateChangeContext(userName, out var context))
        {
            return ValidationProblemResults.Create(HttpContext, ModelState);
        }

        var result = await commands.UpdateStatusAsync(id, request, context, cancellationToken);

        return result.Outcome switch
        {
            StatusUpdateOutcome.Updated => result.Ticket!,
            StatusUpdateOutcome.NotFound => TicketNotFound(id),
            StatusUpdateOutcome.InvalidTransition => ProblemWith(StatusCodes.Status422UnprocessableEntity, "Invalid status transition.", result.Message,
                ("currentStatus", result.Ticket!.Status), ("requestedStatus", request.NewStatus), ("allowedStatuses", result.AllowedStatuses)),
            StatusUpdateOutcome.Conflict => ProblemWith(StatusCodes.Status409Conflict, "Concurrency conflict.", result.Message,
                ("currentTicket", result.Ticket)),
            _ => throw new InvalidOperationException($"Unexpected outcome {result.Outcome}."),
        };
    }

    private bool TryCreateChangeContext(string? userName, out StatusChangeContext context)
    {
        var trimmed = userName?.Trim();
        context = new StatusChangeContext(string.IsNullOrEmpty(trimmed) ? DefaultUserName : trimmed, HttpContext.TraceIdentifier);

        if (trimmed?.Length > TicketStatusHistory.ChangedByMaxLength)
        {
            ModelState.AddModelError(UserNameHeader, $"'{UserNameHeader}' must be {TicketStatusHistory.ChangedByMaxLength} characters or fewer.");
            return false;
        }

        return true;
    }

    private ObjectResult TicketNotFound(int id) =>
        ProblemWith(StatusCodes.Status404NotFound, "Ticket not found.", $"Ticket {id} does not exist.");

    private ObjectResult ProblemWith(int statusCode, string title, string? detail, params (string Key, object? Value)[] extensions)
    {
        var problem = ProblemDetailsFactory.CreateProblemDetails(HttpContext, statusCode, title, detail: detail);
        foreach (var (key, value) in extensions)
        {
            problem.Extensions[key] = value;
        }

        return new ObjectResult(problem) { StatusCode = statusCode, ContentTypes = { "application/problem+json" } };
    }
}
