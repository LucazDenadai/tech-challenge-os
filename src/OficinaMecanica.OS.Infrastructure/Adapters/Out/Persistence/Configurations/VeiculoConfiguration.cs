using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OficinaMecanica.OS.Domain.Entities;

namespace OficinaMecanica.OS.Infrastructure.Adapters.Out.Persistence.Configurations;

public class VeiculoConfiguration : IEntityTypeConfiguration<Veiculo>
{
    public void Configure(EntityTypeBuilder<Veiculo> builder)
    {
        builder.ToTable("Veiculos");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Placa).HasMaxLength(10).IsRequired();
        builder.HasIndex(v => v.Placa).IsUnique();
        builder.Property(v => v.Marca).HasMaxLength(50).IsRequired();
        builder.Property(v => v.Modelo).HasMaxLength(50).IsRequired();
        builder.Property(v => v.Cor).HasMaxLength(30);
    }
}
