using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OficinaMecanica.OS.Domain.Entities;

namespace OficinaMecanica.OS.Infrastructure.Adapters.Out.Persistence.Configurations;

public class HistoricoStatusOSConfiguration : IEntityTypeConfiguration<HistoricoStatusOS>
{
    public void Configure(EntityTypeBuilder<HistoricoStatusOS> builder)
    {
        builder.ToTable("HistoricoStatusOS");
        builder.HasKey(h => h.Id);
        builder.Property(h => h.StatusAnterior).IsRequired();
        builder.Property(h => h.StatusNovo).IsRequired();
        builder.Property(h => h.DataAlteracao).IsRequired();
    }
}
