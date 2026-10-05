using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OficinaMecanica.OS.Domain.Entities;

namespace OficinaMecanica.OS.Infrastructure.Adapters.Out.Persistence.Configurations;

public class FilialConfiguration : IEntityTypeConfiguration<Filial>
{
    public void Configure(EntityTypeBuilder<Filial> builder)
    {
        builder.ToTable("Filiais");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Codigo).HasMaxLength(20).IsRequired();
        builder.HasIndex(f => f.Codigo).IsUnique();
        builder.Property(f => f.Nome).HasMaxLength(100).IsRequired();
        builder.Property(f => f.Ativo).HasDefaultValue(true);
    }
}
