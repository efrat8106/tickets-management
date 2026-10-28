using Microsoft.AspNetCore.Mvc;
using Tickets.Application.Common;
using Tickets.Application.Tickets;
using Tickets.Application.Tickets.Dtos;

namespace Tickets.Api.Controllers;

[ApiController]
[Route("api/tickets")]
public sealed class TicketsController(TicketQueryService queries) : ControllerBase
{
    /// <summary>Returns a page of tickets, filtered, searched and sorted in the database.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<TicketListItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<PagedResult<TicketListItemDto>> GetTickets([FromQuery] TicketQuery query, CancellationToken cancellationToken) =>
        queries.GetTicketsAsync(query, cancellationToken);
}
