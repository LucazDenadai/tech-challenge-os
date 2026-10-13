using System.Diagnostics.CodeAnalysis;
using OficinaMecanica.OS.Domain.Entities;

namespace OficinaMecanica.OS.Domain.Saga;

// Mensagem que causou a transição: tipo versionado (ex.: "QuoteApproved.v1") e messageId para o histórico.
public readonly record struct Causa(string Tipo, Guid? MessageId)
{
    public static readonly Causa Abertura = new("AberturaOS", null);
    public static readonly Causa CancelamentoManual = new("CancelamentoManual", null);
}

// Saga orquestrada pelo OS (ADR-017), uma por OS. Id = correlationId de todas as mensagens.
// Cada método aplica uma transição do fluxo normal (CARD-40a). Retorna false quando a mensagem não se aplica
// ao estado atual (atrasada, repetida com outro messageId ou de estado terminal): nada muda.
public class SagaOrdemServico : EntityBase
{
    private static readonly EstadoSaga[] _terminais = [EstadoSaga.Completed, EstadoSaga.Cancelled, EstadoSaga.Compensated];

    public Guid OsId { get; private set; }
    public Guid FilialId { get; private set; }
    public Guid ClienteId { get; private set; }
    public Guid VeiculoId { get; private set; }
    public EstadoSaga Estado { get; private set; }
    public string? Motivo { get; private set; }

    public Guid? ExecutionId { get; private set; }
    public string? MoedaDiagnostico { get; private set; }
    public DateTimeOffset? PrecoSnapshotEm { get; private set; }

    public Guid? QuoteId { get; private set; }
    public int? QuoteVersion { get; private set; }
    public decimal? ValorOrcamento { get; private set; }
    public string? MoedaOrcamento { get; private set; }

    public Guid? ReservationId { get; private set; }
    public Guid? PaymentId { get; private set; }

    private readonly List<ItemDiagnostico> _itens = new();
    public IReadOnlyCollection<ItemDiagnostico> Itens => _itens.AsReadOnly();

    private readonly List<TransicaoSaga> _transicoes = new();
    public IReadOnlyCollection<TransicaoSaga> Transicoes => _transicoes.AsReadOnly();

    public bool Terminada => _terminais.Contains(Estado);

    // Peças a reservar em Operações: só itens do tipo Peca, somados por peça.
    public IReadOnlyList<(Guid PecaId, int Quantidade)> PecasParaReservar => _itens
        .Where(i => i.Tipo == TipoItemDiagnostico.Peca)
        .GroupBy(i => i.ItemId)
        .Select(g => (g.Key, g.Sum(i => i.Quantidade)))
        .ToList();

    [ExcludeFromCodeCoverage]
    protected SagaOrdemServico() { }

    public static SagaOrdemServico Iniciar(OrdemServico os)
    {
        var saga = new SagaOrdemServico
        {
            OsId = os.Id,
            FilialId = os.FilialId,
            ClienteId = os.ClienteId,
            VeiculoId = os.VeiculoId
        };
        saga.Transitar(EstadoSaga.Diagnosing, Causa.Abertura);
        return saga;
    }

    public bool RegistrarDiagnostico(Causa causa, Guid executionId, string moeda, DateTimeOffset precoSnapshotEm, IEnumerable<ItemDiagnostico> itens)
    {
        if (Estado != EstadoSaga.Diagnosing)
            return false;

        ExecutionId = executionId;
        MoedaDiagnostico = moeda;
        PrecoSnapshotEm = precoSnapshotEm;
        _itens.AddRange(itens);
        return Transitar(EstadoSaga.RequestingQuote, causa);
    }

    public bool RejeitarDiagnostico(Causa causa, string motivo)
        => Estado == EstadoSaga.Diagnosing && Transitar(EstadoSaga.Cancelled, causa, motivo);

    public bool RegistrarOrcamento(Causa causa, Guid quoteId, int quoteVersion, decimal valor, string moeda)
    {
        if (Estado != EstadoSaga.RequestingQuote)
            return false;

        QuoteId = quoteId;
        QuoteVersion = quoteVersion;
        ValorOrcamento = valor;
        MoedaOrcamento = moeda;
        return Transitar(EstadoSaga.AwaitingApproval, causa);
    }

    public bool RejeitarOrcamento(Causa causa, string motivo)
        => Estado == EstadoSaga.RequestingQuote && Transitar(EstadoSaga.Cancelled, causa, motivo);

    // Sem peças não há o que reservar, mas o contrato exige reserva para iniciar a execução (ADR-017).
    // Até o contrato tratar esse caso, a Saga para em ação manual em vez de seguir sem reserva.
    public bool AprovarOrcamento(Causa causa, Guid quoteId, int quoteVersion)
    {
        if (Estado != EstadoSaga.AwaitingApproval || !EhOrcamentoVigente(quoteId, quoteVersion))
            return false;

        return PecasParaReservar.Count == 0
            ? Transitar(EstadoSaga.ManualActionRequired, causa, "Diagnóstico sem peças: o contrato exige reserva para iniciar a execução.")
            : Transitar(EstadoSaga.ReservingInventory, causa);
    }

    // Recusa do cliente ou expiração da versão vigente: nada foi reservado nem cobrado.
    public bool EncerrarOrcamento(Causa causa, Guid quoteId, int quoteVersion, string motivo)
        => Estado == EstadoSaga.AwaitingApproval && EhOrcamentoVigente(quoteId, quoteVersion)
           && Transitar(EstadoSaga.Cancelled, causa, motivo);

    public bool RegistrarReserva(Causa causa, Guid reservationId)
    {
        if (Estado != EstadoSaga.ReservingInventory)
            return false;

        ReservationId = reservationId;
        return Transitar(EstadoSaga.CreatingPayment, causa);
    }

    public bool RejeitarReserva(Causa causa, string motivo)
        => Estado == EstadoSaga.ReservingInventory && Transitar(EstadoSaga.Cancelled, causa, motivo);

    public bool RegistrarPagamentoPendente(Causa causa, Guid paymentId)
    {
        if (Estado != EstadoSaga.CreatingPayment)
            return false;

        PaymentId = paymentId;
        return Transitar(EstadoSaga.AwaitingPayment, causa);
    }

    // Aceito também em CreatingPayment: a ordem de entrega entre canais diferentes não é garantida.
    // Valor ou moeda diferentes do orçamento aprovado nunca liberam a execução.
    public bool AprovarPagamento(Causa causa, Guid paymentId, decimal valor, string moeda)
    {
        if (Estado is not (EstadoSaga.CreatingPayment or EstadoSaga.AwaitingPayment))
            return false;
        if (PaymentId is { } conhecido && conhecido != paymentId)
            return false;

        PaymentId = paymentId;
        if (valor != ValorOrcamento || moeda != MoedaOrcamento)
            return Transitar(EstadoSaga.ManualActionRequired, causa, "Pagamento aprovado com valor ou moeda diferentes do orçamento aprovado.");

        return Transitar(EstadoSaga.StartingExecution, causa);
    }

    public bool RegistrarInicioExecucao(Causa causa, Guid executionId)
        => Estado == EstadoSaga.StartingExecution && executionId == ExecutionId && Transitar(EstadoSaga.InExecution, causa);

    public bool ConcluirExecucao(Causa causa, Guid executionId)
        => Estado == EstadoSaga.InExecution && executionId == ExecutionId && Transitar(EstadoSaga.Completed, causa);

    // Cancelamento manual pela API (CARD-37b). No CARD-40a só encerra a Saga; a compensação
    // (liberar reserva, estornar) entra no CARD-40b.
    public bool CancelarManualmente(string motivo)
        => !Terminada && Transitar(EstadoSaga.Cancelled, Causa.CancelamentoManual, motivo);

    private bool EhOrcamentoVigente(Guid quoteId, int quoteVersion) => quoteId == QuoteId && quoteVersion == QuoteVersion;

    private bool Transitar(EstadoSaga novo, Causa causa, string? motivo = null)
    {
        var anterior = _transicoes.Count == 0 ? (EstadoSaga?)null : Estado;
        Estado = novo;
        Motivo = motivo ?? Motivo;
        _transicoes.Add(new TransicaoSaga(Id, anterior, novo, causa.Tipo, causa.MessageId, motivo));
        MarcarAtualizado();
        return true;
    }
}
