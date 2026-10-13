using System.Text.Json.Nodes;
using OficinaMecanica.OS.Application.Contratos.Saga;
using OficinaMecanica.OS.Domain.Saga;
using DomainOS = OficinaMecanica.OS.Domain.Entities.OrdemServico;

namespace OficinaMecanica.OS.UnitTests.Contratos;

// Os comandos que o OS publica, montados a partir da Saga, seguem o JSON Schema da spec (ADR-018).
public class ComandosPublicadosTests
{
    private static readonly Guid _execucao = Guid.NewGuid();
    private static readonly Guid _quote = Guid.NewGuid();
    private static readonly Guid _reserva = Guid.NewGuid();
    private static readonly Guid _causa = Guid.NewGuid();

    [Fact]
    public void Publicados_CorrespondemAosCanaisDaSpecCujoProdutorEOOS()
    {
        var spec = SpecAsyncApi.CanaisProduzidosPeloOS();

        Assert.Equal(spec.Keys.Order(), CatalogoCanaisOS.Publicados.Select(c => c.Endereco).Order());
        foreach (var canal in CatalogoCanaisOS.Publicados)
            Assert.Equal(SpecAsyncApi.NomeVersionado(spec[canal.Endereco]), canal.MessageType);
    }

    public static TheoryData<string> Comandos() => new() { "diagnostico", "orcamento", "reserva", "pagamento", "execucao" };

    [Theory]
    [MemberData(nameof(Comandos))]
    public void Comando_SerializadoConformeASpec(string caso)
    {
        var saga = SagaPronta();
        MensagemSaga comando = caso switch
        {
            "diagnostico" => ComandoSaga.Diagnostico(saga),
            "orcamento" => ComandoSaga.Orcamento(saga, _causa),
            "reserva" => ComandoSaga.Reserva(saga, _causa),
            "pagamento" => ComandoSaga.Pagamento(saga, _causa, "cliente@exemplo.com"),
            "execucao" => ComandoSaga.InicioExecucao(saga, _causa),
            _ => throw new ArgumentOutOfRangeException(nameof(caso))
        };

        var json = JsonNode.Parse(ComandoSaga.Serializar(comando))!;
        var nome = SpecAsyncApi.CanaisProduzidosPeloOS()[CatalogoCanaisOS.Publicado(comando.GetType()).Endereco];

        Assert.True(SpecAsyncApi.Valida(nome, json), $"A spec rejeitou {comando.MessageType}: {json.ToJsonString()}");
        Assert.Equal("os", json["producer"]!.GetValue<string>());
        Assert.Equal(saga.Id.ToString(), json["correlationId"]!.GetValue<string>());
        Assert.Equal(saga.OsId.ToString(), json["osId"]!.GetValue<string>());
        Assert.StartsWith($"os:{saga.OsId}:", json["idempotencyKey"]!.GetValue<string>());
        // Só o primeiro comando da Saga não tem causa (spec, campo causationId).
        Assert.Equal(caso != "diagnostico", json["causationId"] is not null);
    }

    [Fact]
    public void MesmaEtapa_MesmaIdempotencyKeyEmMensagensDiferentes()
    {
        var saga = SagaPronta();

        var primeiro = ComandoSaga.Reserva(saga, Guid.NewGuid());
        var segundo = ComandoSaga.Reserva(saga, Guid.NewGuid());

        Assert.NotEqual(primeiro.MessageId, segundo.MessageId);
        Assert.Equal(primeiro.IdempotencyKey, segundo.IdempotencyKey);
    }

    [Fact]
    public void Pagamento_LevaValorMoedaEVersaoAprovadosEOEmail()
    {
        var comando = ComandoSaga.Pagamento(SagaPronta(), _causa, "cliente@exemplo.com");

        Assert.Equal(_quote, comando.QuoteId);
        Assert.Equal(1, comando.QuoteVersion);
        Assert.Equal(217.70m, comando.Amount);
        Assert.Equal("BRL", comando.Currency);
        Assert.Equal("cliente@exemplo.com", comando.PayerEmail);
    }

    [Fact]
    public void Reserva_SoPecasSomadas()
    {
        var comando = ComandoSaga.Reserva(SagaPronta(), _causa);

        var item = Assert.Single(comando.Items);
        Assert.Equal(3, item.Quantity);
    }

    // Saga com diagnóstico, orçamento e reserva registrados: todos os comandos podem ser montados.
    private static SagaOrdemServico SagaPronta()
    {
        var os = new DomainOS("OS-001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "");
        var saga = SagaOrdemServico.Iniciar(os);
        var peca = Guid.NewGuid();
        saga.RegistrarDiagnostico(new Causa("DiagnosisCompleted.v1", Guid.NewGuid()), _execucao, "BRL", DateTimeOffset.UtcNow,
        [
            new ItemDiagnostico(peca, TipoItemDiagnostico.Peca, "Filtro de óleo", 1, 45.90m),
            new ItemDiagnostico(peca, TipoItemDiagnostico.Peca, "Filtro de óleo", 2, 45.90m),
            new ItemDiagnostico(Guid.NewGuid(), TipoItemDiagnostico.Servico, "Troca de óleo", 1, 80m)
        ]);
        saga.RegistrarOrcamento(new Causa("QuoteReady.v1", Guid.NewGuid()), _quote, 1, 217.70m, "BRL");
        saga.AprovarOrcamento(new Causa("QuoteApproved.v1", Guid.NewGuid()), _quote, 1);
        saga.RegistrarReserva(new Causa("InventoryReserved.v1", Guid.NewGuid()), _reserva);
        return saga;
    }
}
