using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OficinaMecanica.OS.Infrastructure.Adapters.Out.Persistence.Inbox;

namespace OficinaMecanica.OS.Infrastructure.Adapters.Out.Persistence.Configurations;

public class MensagemInboxConfiguration : IEntityTypeConfiguration<MensagemInbox>
{
    public void Configure(EntityTypeBuilder<MensagemInbox> builder)
    {
        builder.ToTable("InboxMensagens");
        builder.HasKey(m => m.MessageId);
        builder.Property(m => m.MessageId).ValueGeneratedNever();
        builder.Property(m => m.MessageType).HasMaxLength(100).IsRequired();
        builder.Property(m => m.Canal).HasMaxLength(200).IsRequired();
        builder.Property(m => m.Producer).HasMaxLength(20).IsRequired();
        builder.Property(m => m.Payload).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(m => m.CorrelationId);
    }
}
