using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OficinaMecanica.OS.Domain.Saga;
using OficinaMecanica.OS.Infrastructure.Adapters.Out.Persistence.Outbox;

namespace OficinaMecanica.OS.Infrastructure.Adapters.Out.Persistence.Configurations;

public class SagaOrdemServicoConfiguration : IEntityTypeConfiguration<SagaOrdemServico>
{
    public void Configure(EntityTypeBuilder<SagaOrdemServico> builder)
    {
        builder.ToTable("SagasOrdemServico");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.HasIndex(s => s.OsId).IsUnique();
        builder.Property(s => s.Estado).IsRequired();
        builder.Property(s => s.Motivo).HasMaxLength(500);
        builder.Property(s => s.MoedaDiagnostico).HasMaxLength(3);
        builder.Property(s => s.MoedaOrcamento).HasMaxLength(3);
        builder.Property(s => s.ValorOrcamento).HasPrecision(12, 2);
        builder.Ignore(s => s.Terminada);
        builder.Ignore(s => s.PecasParaReservar);

        // Coluna de sistema xmin como token de concorrência, como em Billing: duas mensagens da mesma Saga
        // processadas ao mesmo tempo não aplicam transições a partir do mesmo estado. Toda transição
        // atualiza a linha (AtualizadoEm), então a segunda gravação sobre a mesma leitura recebe conflito.
        builder.Property<uint>("Versao").IsRowVersion();

        builder.OwnsMany(s => s.Itens, itens =>
        {
            itens.ToTable("SagaItensDiagnostico");
            itens.WithOwner().HasForeignKey("SagaId");
            itens.Property<int>("Id");
            itens.HasKey("Id");
            itens.Property(i => i.Descricao).HasMaxLength(200).IsRequired();
            itens.Property(i => i.PrecoUnitario).HasPrecision(12, 2);
        });
        builder.Navigation(s => s.Itens).HasField("_itens").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(s => s.Transicoes).WithOne().HasForeignKey(t => t.SagaId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(s => s.Transicoes).HasField("_transicoes").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class TransicaoSagaConfiguration : IEntityTypeConfiguration<TransicaoSaga>
{
    public void Configure(EntityTypeBuilder<TransicaoSaga> builder)
    {
        builder.ToTable("SagaTransicoes");
        builder.HasKey(t => t.Id);
        // Id gerado no domínio: sem isto, o EF trataria a transição nova de uma Saga carregada como Modified.
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Causa).HasMaxLength(100).IsRequired();
        builder.Property(t => t.Motivo).HasMaxLength(500);
        builder.HasIndex(t => t.SagaId);
    }
}

public class MensagemOutboxConfiguration : IEntityTypeConfiguration<MensagemOutbox>
{
    public void Configure(EntityTypeBuilder<MensagemOutbox> builder)
    {
        builder.ToTable("OutboxMensagens");
        builder.HasKey(m => m.MessageId);
        builder.Property(m => m.MessageId).ValueGeneratedNever();
        builder.Property(m => m.MessageType).HasMaxLength(100).IsRequired();
        builder.Property(m => m.Canal).HasMaxLength(200).IsRequired();
        builder.Property(m => m.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(m => m.TraceParent).HasMaxLength(100);
        builder.Property(m => m.UltimoErro).HasMaxLength(500);
        builder.HasIndex(m => m.CorrelationId);
        // O despachante só lê pendentes, em ordem de criação.
        builder.HasIndex(m => m.CriadaEmUtc).HasFilter("\"PublicadaEmUtc\" IS NULL");
    }
}
