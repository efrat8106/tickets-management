using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tickets.Domain;

namespace Tickets.Infrastructure.Persistence.Configurations;

internal sealed class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        builder.ToTable("Tickets", table =>
        {
            table.HasCheckConstraint("CK_Tickets_Status", "[Status] BETWEEN 1 AND 4");
            table.HasCheckConstraint("CK_Tickets_Priority", "[Priority] BETWEEN 1 AND 3");
        });

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Title).HasMaxLength(Ticket.TitleMaxLength).IsRequired();
        builder.Property(t => t.OrganizationName).HasMaxLength(Ticket.OrganizationNameMaxLength).IsRequired();
        builder.Property(t => t.AssignedTo).HasMaxLength(Ticket.AssignedToMaxLength);

        builder.Property(t => t.Status).HasConversion<byte>();
        builder.Property(t => t.Priority).HasConversion<byte>();

        builder.Property(t => t.CreatedAt).HasPrecision(3);
        builder.Property(t => t.UpdatedAt).HasPrecision(3);

        // SQL Server rowversion: incremented by the database on every update and used by EF as the concurrency token.
        builder.Property(t => t.RowVersion).IsRowVersion();

        // Secondary indexes are intentionally not defined yet: they are added after the baseline measurement (T-30/T-31).
    }
}
