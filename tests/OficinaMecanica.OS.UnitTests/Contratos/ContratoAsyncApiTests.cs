using System.Text.Json.Nodes;
using OficinaMecanica.OS.Application.Contratos.Saga;

namespace OficinaMecanica.OS.UnitTests.Contratos;

// Testes de contrato do ADR-018: os DTOs e o validador do OS concordam com o JSON Schema da spec AsyncAPI.
public class ContratoAsyncApiTests
{
    public static TheoryData<string> Canais()
    {
        var dados = new TheoryData<string>();
        foreach (var canal in CatalogoCanaisOS.Todos)
            dados.Add(canal.Endereco);
        return dados;
    }

    // Mutações aplicadas a cada exemplo quando o campo existe; spec e validador devem rejeitar todas.
    private static readonly (string Descricao, Func<JsonObject, bool> Aplicar)[] _mutacoesInvalidas =
    [
        ("messageId não é UUID", m => Trocar(m, "messageId", "nao-e-uuid")),
        ("occurredAtUtc não é data", m => Trocar(m, "occurredAtUtc", "ontem")),
        ("schemaVersion zero", m => Trocar(m, "schemaVersion", 0)),
        ("producer desconhecido", m => Trocar(m, "producer", "estoque")),
        ("osId numérico", m => Trocar(m, "osId", 123)),
        ("currency minúscula", m => Trocar(m, "currency", "brl")),
        ("amount zero", m => Trocar(m, "amount", 0)),
        ("amount com três casas", m => Trocar(m, "amount", 10.001m)),
        ("amount como texto", m => Trocar(m, "amount", "245.90")),
        ("quoteVersion zero", m => Trocar(m, "quoteVersion", 0)),
        ("reason vazio", m => Trocar(m, "reason", "")),
        ("items vazio no diagnóstico", m => m["messageType"]!.GetValue<string>() == "DiagnosisCompleted.v1" && Trocar(m, "items", new JsonArray())),
        ("item com quantidade zero", m => m["messageType"]!.GetValue<string>() == "DiagnosisCompleted.v1" && TrocarNoItem(m, "items", "quantity", 0)),
        ("item com tipo fora do enum", m => TrocarNoItem(m, "items", "type", "Outro")),
        ("item com preço negativo", m => TrocarNoItem(m, "items", "unitPrice", -1)),
        ("peça consumida com quantidade negativa", m => TrocarNoItem(m, "consumedItems", "quantity", -1)),
        ("peça reservada nula", m => m.ContainsKey("reservationId") && m["items"] is JsonArray itens && Substituir(itens))
    ];

    [Fact]
    public void Catalogo_CorrespondeAosCanaisDaSpecCujoConsumidorEOOS()
    {
        var spec = SpecAsyncApi.CanaisConsumidosPeloOS();

        Assert.Equal(spec.Keys.Order(), CatalogoCanaisOS.Todos.Select(c => c.Endereco).Order());
        foreach (var canal in CatalogoCanaisOS.Todos)
            Assert.Equal(SpecAsyncApi.NomeVersionado(spec[canal.Endereco]), canal.MessageType);
    }

    [Theory]
    [MemberData(nameof(Canais))]
    public void ExemploValido_AceitoPelaSpecEPeloValidador(string endereco)
    {
        var canal = CatalogoCanaisOS.Obter(endereco)!;
        var exemplo = MensagensExemplo.Criar(canal);

        Assert.True(SpecAsyncApi.Valida(NomeNaSpec(canal), exemplo), "A spec rejeitou o exemplo; ajuste MensagensExemplo.");
        var leitura = LeitorMensagemSaga.Ler(canal, MensagensExemplo.Bytes(exemplo));
        Assert.True(leitura.Valida, leitura.Motivo);
    }

    [Theory]
    [MemberData(nameof(Canais))]
    public void CampoDesconhecido_AceitoPelaSpecEPeloValidador(string endereco)
    {
        // Campo opcional novo é mudança compatível (ADR-018): o consumidor não pode rejeitar.
        var canal = CatalogoCanaisOS.Obter(endereco)!;
        var exemplo = MensagensExemplo.Criar(canal);
        exemplo["campoNovoOpcional"] = "valor";

        Assert.True(SpecAsyncApi.Valida(NomeNaSpec(canal), exemplo));
        Assert.True(LeitorMensagemSaga.Ler(canal, MensagensExemplo.Bytes(exemplo)).Valida);
    }

    [Theory]
    [MemberData(nameof(Canais))]
    public void CadaCampoObrigatorioDaSpec_AusenteERejeitadoPeloValidador(string endereco)
    {
        var canal = CatalogoCanaisOS.Obter(endereco)!;
        var obrigatorios = SpecAsyncApi.CamposObrigatorios(NomeNaSpec(canal));
        var aceitos = new List<string>();

        foreach (var campo in obrigatorios)
        {
            var exemplo = MensagensExemplo.Criar(canal);
            exemplo.Remove(campo);

            Assert.False(SpecAsyncApi.Valida(NomeNaSpec(canal), exemplo), $"Spec aceitou mensagem sem '{campo}'.");
            if (LeitorMensagemSaga.Ler(canal, MensagensExemplo.Bytes(exemplo)).Valida)
                aceitos.Add(campo);
        }

        Assert.True(aceitos.Count == 0, $"Validador aceitou mensagem sem: {string.Join(", ", aceitos)}");
    }

    [Theory]
    [MemberData(nameof(Canais))]
    public void MutacaoInvalida_RejeitadaPelaSpecEPeloValidador(string endereco)
    {
        var canal = CatalogoCanaisOS.Obter(endereco)!;
        var divergencias = new List<string>();

        foreach (var (descricao, aplicar) in _mutacoesInvalidas)
        {
            var exemplo = MensagensExemplo.Criar(canal);
            if (!aplicar(exemplo)) continue;

            var spec = SpecAsyncApi.Valida(NomeNaSpec(canal), exemplo);
            var validador = LeitorMensagemSaga.Ler(canal, MensagensExemplo.Bytes(exemplo)).Valida;
            if (spec || validador)
                divergencias.Add($"{descricao} (spec aceitou: {spec}, validador aceitou: {validador})");
        }

        Assert.True(divergencias.Count == 0, string.Join("; ", divergencias));
    }

    private static string NomeNaSpec(CanalConsumido canal) => SpecAsyncApi.CanaisConsumidosPeloOS()[canal.Endereco];

    private static bool Trocar(JsonObject mensagem, string campo, JsonNode? valor)
    {
        if (!mensagem.ContainsKey(campo)) return false;
        mensagem[campo] = valor;
        return true;
    }

    private static bool TrocarNoItem(JsonObject mensagem, string lista, string campo, JsonNode valor)
    {
        if (mensagem[lista] is not JsonArray { Count: > 0 } itens || itens[0] is not JsonObject item || !item.ContainsKey(campo))
            return false;
        item[campo] = valor;
        return true;
    }

    private static bool Substituir(JsonArray itens)
    {
        itens.Clear();
        itens.Add(null);
        return true;
    }
}
