using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OficinaMecanica.OS.Application.Ports.Out;
using OficinaMecanica.OS.Application.UseCases.OrdemServico;
using OficinaMecanica.OS.Domain.Entities;
using OficinaMecanica.OS.Domain.Enums;
using OficinaMecanica.OS.Infrastructure.Adapters.Out.Persistence;
using OficinaMecanica.OS.Infrastructure.Adapters.Out.Persistence.Repositories;
using OficinaMecanica.OS.IntegrationTests.Fixtures;

namespace OficinaMecanica.OS.IntegrationTests.Persistence;

[Collection(PostgresCollection.Nome)]
public class OrdemServicoPersistenciaTests(PostgresFixture fixture)
{
    private async Task<(string Conn, Guid ClienteId, Guid VeiculoId, Guid FilialId)> PrepararCadastrosAsync()
    {
        var conn = fixture.NovoBancoVazio();
        await using var db = PostgresFixture.CriarContexto(conn);
        await db.Database.MigrateAsync();

        var filial = new Filial("FILIAL-SP", "Filial SP");
        var cliente = new Cliente("Cliente Teste", "11144477735", "cliente@cliente.example", "11900000000", "Rua A");
        var veiculo = new Veiculo(cliente.Id, "TST1A23", "Ford", "Ka", 2019, "Prata");
        db.AddRange(filial, cliente, veiculo);
        await db.SaveChangesAsync();

        return (conn, cliente.Id, veiculo.Id, filial.Id);
    }

    private static AbrirOrdemServicoUseCase CriarAbrirUseCase(AppDbContext db)
        => new(new OrdemServicoRepository(db), new VeiculoRepository(db), new FilialRepository(db), NullLogger<AbrirOrdemServicoUseCase>.Instance);

    private static AtualizarStatusOSUseCase CriarAtualizarUseCase(AppDbContext db)
        => new(new OrdemServicoRepository(db), new EmailNoOp(), new ClienteRepository(db), NullLogger<AtualizarStatusOSUseCase>.Instance);

    [Fact]
    public async Task AbrirETransicionar_PersisteOSComFilialEHistorico()
    {
        var (conn, clienteId, veiculoId, filialId) = await PrepararCadastrosAsync();

        Guid osId;
        await using (var db = PostgresFixture.CriarContexto(conn))
            osId = (await CriarAbrirUseCase(db).ExecutarAsync(new AbrirOrdemServicoRequest(clienteId, veiculoId, filialId, "Barulho no motor"))).Id;

        await using (var db = PostgresFixture.CriarContexto(conn))
            await CriarAtualizarUseCase(db).ExecutarAsync(osId, StatusOrdemServico.AguardandoAprovacao);

        await using (var db = PostgresFixture.CriarContexto(conn))
            await CriarAtualizarUseCase(db).ExecutarAsync(osId, StatusOrdemServico.AguardandoPagamento);

        await using var leitura = PostgresFixture.CriarContexto(conn);
        var os = await new OrdemServicoRepository(leitura).ObterComDetalhesAsync(osId);

        Assert.NotNull(os);
        Assert.Equal(filialId, os.FilialId);
        Assert.Equal(StatusOrdemServico.AguardandoPagamento, os.Status);
        Assert.Equal(
            [StatusOrdemServico.AguardandoAprovacao, StatusOrdemServico.AguardandoPagamento],
            os.Historico.OrderBy(h => h.DataAlteracao).Select(h => h.StatusNovo));
    }

    [Fact]
    public async Task TransicaoInvalida_EstadoEHistoricoNoBancoPermanecemInalterados()
    {
        var (conn, clienteId, veiculoId, filialId) = await PrepararCadastrosAsync();

        Guid osId;
        await using (var db = PostgresFixture.CriarContexto(conn))
            osId = (await CriarAbrirUseCase(db).ExecutarAsync(new AbrirOrdemServicoRequest(clienteId, veiculoId, filialId, "Obs"))).Id;

        await using (var db = PostgresFixture.CriarContexto(conn))
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                CriarAtualizarUseCase(db).ExecutarAsync(osId, StatusOrdemServico.EmExecucao));
            Assert.Contains(nameof(StatusOrdemServico.AguardandoAprovacao), ex.Message);
        }

        await using var leitura = PostgresFixture.CriarContexto(conn);
        var os = await leitura.OrdensServico.Include(o => o.Historico).SingleAsync(o => o.Id == osId);
        Assert.Equal(StatusOrdemServico.EmDiagnostico, os.Status);
        Assert.Empty(os.Historico);
    }

    [Fact]
    public async Task OSComFilialInexistente_BancoRejeitaPorIntegridadeReferencial()
    {
        var (conn, clienteId, veiculoId, _) = await PrepararCadastrosAsync();
        await using var db = PostgresFixture.CriarContexto(conn);

        db.OrdensServico.Add(new OrdemServico("OS-X", clienteId, veiculoId, Guid.NewGuid(), "Obs"));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private sealed class EmailNoOp : IEmailPort
    {
        public Task EnviarAtualizacaoStatusAsync(string destinatario, string numeroOS, StatusOrdemServico novoStatus, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
