using Microsoft.EntityFrameworkCore;
using Tickets.Domain;

namespace Tickets.Application.Abstractions;

/// <summary>
/// The persistence surface the application services need. Services query <see cref="DbSet{TEntity}"/> directly
/// (IQueryable, executed in the database) – no repository layer on top of EF.
/// </summary>
public interface ITicketsDbContext
{
    DbSet<Ticket> Tickets { get; }
    DbSet<TicketStatusHistory> TicketStatusHistory { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
