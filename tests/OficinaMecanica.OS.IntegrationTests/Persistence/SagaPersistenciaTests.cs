using Microsoft.EntityFrameworkCore;
using OficinaMecanica.OS.Domain.Entities;
using OficinaMecanica.OS.Domain.Saga;
using OficinaMecanica.OS.Infrastructure.Adapters.Out.Persistence.Repositories;
using OficinaMecanica.OS.IntegrationTests.Fixtures;

namespace OficinaMecanica.OS.IntegrationTests.Persistence;

[Collection(PostgresCollection.Nome)]
public class SagaPersistenciaTests(PostgresFixture fixture)
{
    private static readonly Guid _peca = Guid.NewGuid();

    private async Task<(string Conn, Guid OsId)> PrepararSagaAsync()
    {
        var conn = fixture.NovoBancoVazio();
        await using var db = PostgresFixture.CriarContexto(conn);
        await db.Database.MigrateAsync();

        var filial = new Filial("FILIAL-SP", "Filial SP");
        var cliente = new Cliente("Cliente Teste", "11144477735", "cliente@cliente.example", "11900000000", "Rua A");
        var veiculo = new Veiculo(cliente.Id, "TST1A23", "Ford", "Ka", 2019, "Prata");
        var os = new OrdemServico("OS-0001", cliente.Id, veiculo.Id, filial.Id, "");
        db.AddRange(filial, cliente, veiculo, os);
        await new SagaRepository(db).AdicionarAsync(SagaOrdemServico.Iniciar(os));
        await db.SaveChangesAsync();
        return (conn, os.Id);
    }

    private static Causa C(string tipo) => new(tipo, Guid.NewGuid());

    [Fact]
    public async Task TransicoesEmContextosDiferentes_PersistemItensEHistorico()
    {
        var (conn, osId) = await PrepararSagaAsync();
        var execucao = Guid.NewGuid();

        await using (var db = PostgresFixture.CriarContexto(conn))
        {
            var saga = (await new SagaRepository(db).ObterPorOsAsync(osId))!;
            Assert.True(saga.RegistrarDiagnostico(C("DiagnosisCompleted.v1"), execucao, "BRL", DateTimeOffset.UtcNow,
            [
                new ItemDiagnostico(_peca, TipoItemDiagnostico.Peca, "Filtro", 2, 45.90m),
                new ItemDiagnostico(Guid.NewGuid(), TipoItemDiagnostico.Servico, "Troca", 1, 80m)
            ]));
            await db.SaveChangesAsync();
        }

        await using (var db = PostgresFixture.CriarContexto(conn))
        {
            var saga = (await new SagaRepository(db).ObterPorOsAsync(osId))!;
            Assert.True(saga.RegistrarOrcamento(C("QuoteReady.v1"), Guid.NewGuid(), 1, 171.80m, "BRL"));
            await db.SaveChangesAsync();
        }

        await using var leitura = PostgresFixture.CriarContexto(conn);
        var lida = (await new SagaRepository(leitura).ObterPorOsAsync(osId))!;
        Assert.Equal(EstadoSaga.AwaitingApproval, lida.Estado);
        Assert.Equal(execucao, lida.ExecutionId);
        Assert.Equal(171.80m, lida.ValorOrcamento);
        Assert.Equal(2, lida.Itens.Count);
        Assert.Equal((_peca, 2), Assert.Single(lida.PecasParaReservar));
        Assert.Equal(
            [EstadoSaga.Diagnosing, EstadoSaga.RequestingQuote, EstadoSaga.AwaitingApproval],
            lida.Transicoes.OrderBy(t => t.Em).Select(t => t.EstadoNovo));
    }

    [Fact]
    public async Task DuasTransicoesSobreAMesmaLeitura_SegundaRecebeConflito()
    {
        var (conn, osId) = await PrepararSagaAsync();

        await using var primeiro = PostgresFixture.CriarContexto(conn);
        await using var segundo = PostgresFixture.CriarContexto(conn);
        var sagaA = (await new SagaRepository(primeiro).ObterPorOsAsync(osId))!;
        var sagaB = (await new SagaRepository(segundo).ObterPorOsAsync(osId))!;

        Assert.True(sagaA.RejeitarDiagnostico(C("DiagnosisRejected.v1"), "Filial inválida"));
        await primeiro.SaveChangesAsync();

        Assert.True(sagaB.RegistrarDiagnostico(C("DiagnosisCompleted.v1"), Guid.NewGuid(), "BRL", DateTimeOffset.UtcNow,
            [new ItemDiagnostico(_peca, TipoItemDiagnostico.Peca, "Filtro", 1, 45.90m)]));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => segundo.SaveChangesAsync());
    }
}
