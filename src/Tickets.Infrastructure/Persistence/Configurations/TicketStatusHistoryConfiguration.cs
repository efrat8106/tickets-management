using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tickets.Domain;

namespace Tickets.Infrastructure.Persistence.Configurations;

internal sealed class TicketStatusHistoryConfiguration : IEntityTypeConfiguration<TicketStatusHistory>
{
    public void Configure(EntityTypeBuilder<TicketStatusHistory> builder)
    {
        builder.ToTable("TicketStatusHistory", table =>
        {
            table.HasCheckConstraint("CK_TicketStatusHistory_PreviousStatus", "[PreviousStatus] BETWEEN 1 AND 4");
            table.HasCheckConstraint("CK_TicketStatusHistory_NewStatus", "[NewStatus] BETWEEN 1 AND 4");
        });

        builder.HasKey(h => h.Id);

        builder.Property(h => h.PreviousStatus).HasConversion<byte>();
        builder.Property(h => h.NewStatus).HasConversion<byte>();
        builder.Property(h => h.ChangedAt).HasPrecision(3);
        builder.Property(h => h.ChangedBy).HasMaxLength(TicketStatusHistory.ChangedByMaxLength).IsRequired();
        builder.Property(h => h.CorrelationId).HasMaxLength(TicketStatusHistory.CorrelationIdMaxLength).IsUnicode(false);

        // Tickets are never deleted; an audit trail must not disappear through a cascade.
        builder.HasOne<Ticket>()
            .WithMany()
            .HasForeignKey(h => h.RequestId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
