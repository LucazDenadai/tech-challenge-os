using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using OficinaMecanica.OS.Domain.Entities;
using OficinaMecanica.OS.Domain.Saga;
using OficinaMecanica.OS.Infrastructure.Adapters.Out.Persistence.Inbox;
using OficinaMecanica.OS.Infrastructure.Adapters.Out.Persistence.Outbox;

namespace OficinaMecanica.OS.Infrastructure.Adapters.Out.Persistence;

public class AppDbContext : DbContext
{
    [ExcludeFromCodeCoverage]
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<Veiculo> Veiculos => Set<Veiculo>();
    public DbSet<Filial> Filiais => Set<Filial>();
    [ExcludeFromCodeCoverage] public DbSet<OrdemServico> OrdensServico => Set<OrdemServico>();
    [ExcludeFromCodeCoverage] public DbSet<HistoricoStatusOS> HistoricoStatusOS => Set<HistoricoStatusOS>();
    [ExcludeFromCodeCoverage] public DbSet<MensagemInbox> InboxMensagens => Set<MensagemInbox>();
    public DbSet<SagaOrdemServico> Sagas => Set<SagaOrdemServico>();
    public DbSet<MensagemOutbox> Outbox => Set<MensagemOutbox>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
