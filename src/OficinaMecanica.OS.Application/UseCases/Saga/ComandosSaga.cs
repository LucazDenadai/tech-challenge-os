using System.Diagnostics.Metrics;
using OficinaMecanica.OS.Application.Contratos.Saga;
using OficinaMecanica.OS.Application.Exceptions;
using OficinaMecanica.OS.Application.Ports.Out;
using OficinaMecanica.OS.Domain.Saga;

namespace OficinaMecanica.OS.Application.UseCases.Saga;

// Grava na outbox os comandos de uma etapa da Saga. Único ponto usado pela transição de mensagem, pelo
// cancelamento manual, pela resolução do Admin e pelo reenvio por prazo: a mesma etapa gera sempre os mesmos
// comandos, com a mesma idempotencyKey.
public class ComandosSaga(IClienteRepository clientes, IOutbox outbox)
{
    public async Task GravarAsync(SagaOrdemServico saga, IReadOnlyCollection<ComandoEtapa> etapas, Guid? causationId, CancellationToken ct = default)
    {
        if (etapas.Count == 0)
            return;

        // O e-mail só é lido quando a etapa pede pagamento (payerEmail, ADR-019).
        string? email = null;
        if (etapas.Contains(ComandoEtapa.Pagamento))
            email = (await clientes.ObterPorIdAsync(saga.ClienteId, ct) ?? throw new NotFoundException("Cliente", saga.ClienteId)).Email;

        foreach (var comando in ComandoSaga.DaEtapa(saga, etapas, causationId, email))
            outbox.Adicionar(comando);
    }
}

// Parâmetros de prazo e reenvio da Saga (CARD-40b). Valores padrão do MVP.
public class ConfiguracaoSaga
{
    public const string Secao = "Saga";

    // Fração de minuto aceita, para testes de integração com prazo curto.
    public double PrazoRespostaMinutos { get; set; } = 5;
    public int MaxReenvios { get; set; } = 3;
    public bool VerificacaoHabilitada { get; set; } = true;
    public int IntervaloVerificacaoSegundos { get; set; } = 30;
    // Leitura dos estados para as métricas (CARD-42a).
    public int IntervaloMetricasSegundos { get; set; } = 30;

    public TimeSpan Prazo => TimeSpan.FromMinutes(PrazoRespostaMinutos);
}

public static class MetricasAcaoManual
{
    private static readonly Meter _medidor = new(Mensageria.MetricasSaga.NomeMedidor);
    private static readonly Counter<long> _acaoManual = _medidor.CreateCounter<long>(
        "os_saga_acao_manual", description: "Sagas que pararam esperando ação manual, por causa.");

    public static void Registrar(CausaAcaoManual causa) => _acaoManual.Add(1, new KeyValuePair<string, object?>("causa", causa.ToString()));
}
