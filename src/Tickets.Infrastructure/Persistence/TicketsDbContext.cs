using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Tickets.Application.Abstractions;
using Tickets.Domain;

namespace Tickets.Infrastructure.Persistence;

public sealed class TicketsDbContext(DbContextOptions<TicketsDbContext> options) : DbContext(options), ITicketsDbContext
{
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketStatusHistory> TicketStatusHistory => Set<TicketStatusHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TicketsDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // All timestamps are stored in UTC; datetime2 has no offset, so values read back are marked as UTC.
        // Without this they would be serialized without "Z" and clients would treat them as local time.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        value => value,
        value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
