using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OficinaMecanica.OS.Domain.Entities;

namespace OficinaMecanica.OS.Infrastructure.Adapters.Out.Persistence.Configurations;

public class OrdemServicoConfiguration : IEntityTypeConfiguration<OrdemServico>
{
    public void Configure(EntityTypeBuilder<OrdemServico> builder)
    {
        builder.ToTable("OrdensServico");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Numero).HasMaxLength(20).IsRequired();
        builder.HasIndex(o => o.Numero).IsUnique();
        builder.Property(o => o.Status).IsRequired();
        builder.Property(o => o.Observacoes).HasMaxLength(500);
        builder.HasOne(o => o.Cliente).WithMany().HasForeignKey(o => o.ClienteId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(o => o.Veiculo).WithMany().HasForeignKey(o => o.VeiculoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(o => o.Filial).WithMany().HasForeignKey(o => o.FilialId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(o => o.Historico).WithOne().HasForeignKey(h => h.OrdemServicoId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(o => o.Historico).HasField("_historico").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
