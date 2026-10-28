using Microsoft.AspNetCore.Mvc;
using Tickets.Application.Common;
using Tickets.Application.Tickets;
using Tickets.Application.Tickets.Dtos;

namespace Tickets.Api.Controllers;

[ApiController]
[Route("api/tickets")]
public sealed class TicketsController(TicketQueryService queries, TicketSummaryService summary) : ControllerBase
{
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

    private ObjectResult TicketNotFound(int id) =>
        Problem(statusCode: StatusCodes.Status404NotFound, title: "Ticket not found.", detail: $"Ticket {id} does not exist.");
}
