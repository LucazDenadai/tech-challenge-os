using System.Diagnostics.CodeAnalysis;
using OficinaMecanica.OS.Domain.Entities;

namespace OficinaMecanica.OS.Domain.Saga;

// Mensagem ou ação que causou a transição: tipo (ex.: "QuoteApproved.v1") e messageId para o histórico.
public readonly record struct Causa(string Tipo, Guid? MessageId)
{
    public static readonly Causa Abertura = new("AberturaOS", null);
    public static readonly Causa CancelamentoManual = new("CancelamentoManual", null);
    public static readonly Causa PrazoVencido = new("PrazoVencido", null);
    public static readonly Causa ResolucaoAdmin = new("ResolucaoAdmin", null);
}

// Saga orquestrada pelo OS (ADR-017), uma por OS. Id = correlationId de todas as mensagens.
// Fluxo normal (CARD-40a) e compensações (CARD-40b, ADR-020). Cada método de evento retorna false quando a
// mensagem não se aplica ao estado atual (atrasada, repetida com outro messageId ou de estado terminal): nada muda.
// Os comandos que cada etapa precisa vêm de ComandosDaEtapa, usado na transição, no reenvio por prazo e na retomada.
public class SagaOrdemServico : EntityBase
{
    private static readonly EstadoSaga[] _terminais = [EstadoSaga.Completed, EstadoSaga.Cancelled, EstadoSaga.Compensated];

    // Estados que esperam resposta automática de um participante: têm prazo e reenvio (CARD-40b).
    public static readonly EstadoSaga[] EstadosComPrazo =
    [
        EstadoSaga.RequestingQuote, EstadoSaga.ReservingInventory, EstadoSaga.CreatingPayment, EstadoSaga.StartingExecution,
        EstadoSaga.ReleasingReservation, EstadoSaga.Compensating, EstadoSaga.CancellingExecution
    ];

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
    public bool PagamentoAprovado { get; private set; }

    // Compensação: o que já foi confirmado pelos participantes.
    public bool EstornoConfirmado { get; private set; }
    public bool EstornoPendente { get; private set; }
    public bool LiberacaoConfirmada { get; private set; }

    // Cancelamento pedido enquanto um participante responde: a resposta decide a compensação (ADR-020).
    public bool CancelamentoSolicitado { get; private set; }

    // Ação manual: por que parou e para onde a retomada volta.
    public CausaAcaoManual? CausaManual { get; private set; }
    public EstadoSaga? EstadoRetomada { get; private set; }

    // Prazo da etapa atual: início e reenvios já feitos.
    public DateTime EtapaIniciadaEm { get; private set; } = DateTime.UtcNow;
    public int Reenvios { get; private set; }

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

    // Comandos que a etapa atual precisa ter enviado. Repetir é seguro: a idempotencyKey é a mesma.
    public IReadOnlyList<ComandoEtapa> ComandosDaEtapa() => Estado switch
    {
        EstadoSaga.Diagnosing => [ComandoEtapa.Diagnostico],
        EstadoSaga.RequestingQuote => [ComandoEtapa.Orcamento],
        EstadoSaga.ReservingInventory => [ComandoEtapa.Reserva],
        EstadoSaga.CreatingPayment => [ComandoEtapa.Pagamento],
        EstadoSaga.StartingExecution => [ComandoEtapa.InicioExecucao],
        EstadoSaga.ReleasingReservation => [ComandoEtapa.Liberacao],
        EstadoSaga.CancellingExecution => [ComandoEtapa.CancelamentoExecucao],
        EstadoSaga.Compensating => ComandosDeCompensacao(),
        _ => []
    };

    // ── Fluxo normal (CARD-40a) ────────────────────────────────────────────────

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

    // Sem peças (serviço sem peça, ADR-020) não há reserva: a Saga vai direto ao pagamento.
    public bool AprovarOrcamento(Causa causa, Guid quoteId, int quoteVersion)
    {
        if (Estado != EstadoSaga.AwaitingApproval || !EhOrcamentoVigente(quoteId, quoteVersion))
            return false;

        return Transitar(PecasParaReservar.Count == 0 ? EstadoSaga.CreatingPayment : EstadoSaga.ReservingInventory, causa);
    }

    // Recusa do cliente ou expiração da versão vigente: nada foi reservado nem cobrado.
    public bool EncerrarOrcamento(Causa causa, Guid quoteId, int quoteVersion, string motivo)
        => Estado == EstadoSaga.AwaitingApproval && EhOrcamentoVigente(quoteId, quoteVersion)
           && Transitar(EstadoSaga.Cancelled, causa, motivo);

    // Com cancelamento pedido, a reserva confirmada é liberada em vez de seguir ao pagamento.
    public bool RegistrarReserva(Causa causa, Guid reservationId)
    {
        if (Estado != EstadoSaga.ReservingInventory)
            return false;

        ReservationId = reservationId;
        return CancelamentoSolicitado
            ? Transitar(EstadoSaga.ReleasingReservation, causa, Motivo)
            : Transitar(EstadoSaga.CreatingPayment, causa);
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

    // Aceito também em CreatingPayment (a ordem entre canais não é garantida) e em ReconcilingPayment.
    // Valor ou moeda divergentes nunca liberam a execução. Com cancelamento pedido, o pagamento é estornado.
    public bool AprovarPagamento(Causa causa, Guid paymentId, decimal valor, string moeda)
    {
        if (Estado is not (EstadoSaga.CreatingPayment or EstadoSaga.AwaitingPayment or EstadoSaga.ReconcilingPayment)
            || !EhPagamentoDaSaga(paymentId))
            return false;

        PaymentId = paymentId;
        PagamentoAprovado = true;
        if (valor != ValorOrcamento || moeda != MoedaOrcamento)
            return PararParaAcaoManual(causa, CausaAcaoManual.PagamentoDivergente,
                "Pagamento aprovado com valor ou moeda diferentes do orçamento aprovado.");

        return CancelamentoSolicitado
            ? Transitar(EstadoSaga.Compensating, causa, Motivo)
            : Transitar(EstadoSaga.StartingExecution, causa);
    }

    // Com cancelamento pedido, a execução que acabou de entrar na fila recebe o pedido de cancelamento.
    public bool RegistrarInicioExecucao(Causa causa, Guid executionId)
    {
        if (Estado != EstadoSaga.StartingExecution || executionId != ExecutionId)
            return false;

        return CancelamentoSolicitado
            ? Transitar(EstadoSaga.CancellingExecution, causa, Motivo)
            : Transitar(EstadoSaga.InExecution, causa);
    }

    // Aceito também em CancellingExecution: o reparo pode terminar enquanto o pedido de cancelamento viaja.
    // Aceito também em StartingExecution: ExecutionStarted e ExecutionCompleted vêm por filas diferentes, e a conclusão
    // pode ser processada primeiro. Se a execução terminou, ela começou.
    public bool ConcluirExecucao(Causa causa, Guid executionId)
        => Estado is EstadoSaga.StartingExecution or EstadoSaga.InExecution or EstadoSaga.CancellingExecution
           && executionId == ExecutionId
           && Transitar(EstadoSaga.Completed, causa);

    // ── Pagamento recusado ou incerto (CARD-40b) ───────────────────────────────

    public bool RegistrarResultadoDesconhecido(Causa causa, Guid paymentId, string motivo)
    {
        if (Estado is not (EstadoSaga.CreatingPayment or EstadoSaga.AwaitingPayment) || !EhPagamentoDaSaga(paymentId))
            return false;

        PaymentId = paymentId;
        return Transitar(EstadoSaga.ReconcilingPayment, causa, motivo);
    }

    // Recusa ou expiração: libera a reserva; sem reserva (serviço sem peça) encerra direto.
    public bool RecusarPagamento(Causa causa, Guid paymentId, string motivo)
    {
        if (Estado is not (EstadoSaga.CreatingPayment or EstadoSaga.AwaitingPayment or EstadoSaga.ReconcilingPayment)
            || !EhPagamentoDaSaga(paymentId))
            return false;

        PaymentId = paymentId;
        return Transitar(ReservationId is null ? EstadoSaga.Cancelled : EstadoSaga.ReleasingReservation, causa, motivo);
    }

    // ── Execução recusada, falha ou cancelada depois do pagamento (CARD-40b) ───

    public bool RejeitarInicioExecucao(Causa causa, Guid executionId, string motivo)
        => Estado == EstadoSaga.StartingExecution && executionId == ExecutionId
           && Transitar(EstadoSaga.Compensating, causa, motivo);

    // Mesma regra de ordem da conclusão: a falha pode chegar antes do ExecutionStarted.
    public bool FalharExecucao(Causa causa, Guid executionId, string motivo)
        => Estado is EstadoSaga.StartingExecution or EstadoSaga.InExecution or EstadoSaga.CancellingExecution
           && executionId == ExecutionId
           && Transitar(EstadoSaga.Compensating, causa, motivo);

    public bool RegistrarCancelamentoExecucao(Causa causa, Guid executionId)
        => Estado == EstadoSaga.CancellingExecution && executionId == ExecutionId
           && Transitar(EstadoSaga.Compensating, causa, Motivo);

    // O reparo já começou: o cancelamento é recusado e a execução segue (ADR-020).
    public bool RecusarCancelamentoExecucao(Causa causa, Guid executionId, string motivo)
    {
        if (Estado != EstadoSaga.CancellingExecution || executionId != ExecutionId)
            return false;

        CancelamentoSolicitado = false;
        return Transitar(EstadoSaga.InExecution, causa, motivo);
    }

    // ── Confirmações da compensação (CARD-40b) ─────────────────────────────────

    public bool RegistrarLiberacao(Causa causa, Guid reservationId)
    {
        if (reservationId != ReservationId)
            return false;

        if (Estado == EstadoSaga.ReleasingReservation)
        {
            LiberacaoConfirmada = true;
            return Transitar(EstadoSaga.Cancelled, causa, Motivo);
        }

        if (Estado != EstadoSaga.Compensating || LiberacaoConfirmada)
            return false;

        LiberacaoConfirmada = true;
        return ConcluirCompensacaoOuRegistrar(causa);
    }

    public bool RegistrarEstorno(Causa causa, Guid paymentId)
    {
        if (Estado != EstadoSaga.Compensating || paymentId != PaymentId || EstornoConfirmado)
            return false;

        EstornoConfirmado = true;
        EstornoPendente = false;
        return ConcluirCompensacaoOuRegistrar(causa);
    }

    // Estorno aceito, mas ainda em processamento no provedor: a Saga não se declara compensada (ADR-017).
    public bool RegistrarEstornoPendente(Causa causa, Guid paymentId)
    {
        if (Estado != EstadoSaga.Compensating || paymentId != PaymentId || EstornoConfirmado)
            return false;

        EstornoPendente = true;
        return Registrar(causa, "Estorno pendente no provedor.");
    }

    public bool FalharEstorno(Causa causa, Guid paymentId, string motivo)
        => Estado == EstadoSaga.Compensating && paymentId == PaymentId && !EstornoConfirmado
           && PararParaAcaoManual(causa, CausaAcaoManual.EstornoFalhou, motivo);

    // ── Efeito confirmado depois do encerramento ───────────────────────────────

    // Reserva confirmada depois que a Saga terminou (por exemplo, cancelada por prazo): precisa ser liberada,
    // senão o estoque fica preso. Retorna true quando a aplicação deve pedir a liberação.
    public bool RegistrarReservaAposEncerramento(Causa causa, Guid reservationId)
    {
        if (!Terminada || Estado == EstadoSaga.Completed || ReservationId is not null)
            return false;

        ReservationId = reservationId;
        return Registrar(causa, "Reserva confirmada depois do encerramento: liberação pedida.");
    }

    // Pagamento aprovado depois que a Saga terminou: precisa ser estornado. Retorna true quando a aplicação
    // deve pedir o estorno.
    public bool RegistrarPagamentoAposEncerramento(Causa causa, Guid paymentId)
    {
        if (!Terminada || Estado == EstadoSaga.Completed || PagamentoAprovado)
            return false;

        PaymentId = paymentId;
        PagamentoAprovado = true;
        return Registrar(causa, "Pagamento aprovado depois do encerramento: estorno pedido.");
    }

    // ── Cancelamento manual e resolução pelo Admin (ADR-020) ───────────────────

    public ResultadoCancelamento SolicitarCancelamento(string motivo)
    {
        switch (Estado)
        {
            case EstadoSaga.Diagnosing or EstadoSaga.RequestingQuote or EstadoSaga.AwaitingApproval:
                Transitar(EstadoSaga.Cancelled, Causa.CancelamentoManual, motivo);
                return ResultadoCancelamento.Encerrada;

            case EstadoSaga.ReservingInventory or EstadoSaga.CreatingPayment or EstadoSaga.AwaitingPayment
                or EstadoSaga.ReconcilingPayment or EstadoSaga.StartingExecution:
                // Um participante está respondendo: a resposta decide a compensação.
                if (!CancelamentoSolicitado)
                {
                    CancelamentoSolicitado = true;
                    Motivo = motivo;
                    Registrar(Causa.CancelamentoManual, motivo);
                }
                return ResultadoCancelamento.EmAndamento;

            case EstadoSaga.InExecution:
                CancelamentoSolicitado = true;
                Transitar(EstadoSaga.CancellingExecution, Causa.CancelamentoManual, motivo);
                return ResultadoCancelamento.EmAndamento;

            case EstadoSaga.ReleasingReservation or EstadoSaga.Compensating or EstadoSaga.CancellingExecution:
                return ResultadoCancelamento.EmAndamento;

            default:
                return ResultadoCancelamento.Recusado;
        }
    }

    public void ResolverAcaoManual(AcaoResolucao acao, string motivo)
    {
        if (Estado != EstadoSaga.ManualActionRequired)
            throw new InvalidOperationException($"A Saga em '{Estado}' não está esperando ação manual.");
        if (string.IsNullOrWhiteSpace(motivo))
            throw new ArgumentException("O motivo da resolução é obrigatório.", nameof(motivo));

        if (acao == AcaoResolucao.Retomar)
        {
            if (CausaManual == CausaAcaoManual.PagamentoDivergente)
                throw new InvalidOperationException("Pagamento divergente não pode ser retomado: só cancelado com estorno.");

            var destino = EstadoRetomada!.Value;
            LimparAcaoManual();
            Transitar(destino, Causa.ResolucaoAdmin, motivo);
            return;
        }

        LimparAcaoManual();
        CancelamentoSolicitado = true;
        var final = PagamentoAprovado && !EstornoConfirmado ? EstadoSaga.Compensating
            : ReservationId is not null && !LiberacaoConfirmada ? EstadoSaga.ReleasingReservation
            : EstadoSaga.Cancelled;
        Transitar(final, Causa.ResolucaoAdmin, motivo);
    }

    // ── Prazos (CARD-40b) ──────────────────────────────────────────────────────

    public bool PrazoVencido(DateTime agora, TimeSpan prazo)
        => EstadosComPrazo.Contains(Estado) && agora >= EtapaIniciadaEm + prazo * (Reenvios + 1);

    // Prazo vencido com reenvios sobrando: a aplicação reenvia ComandosDaEtapa com a mesma idempotencyKey.
    public void RegistrarReenvio()
    {
        Reenvios++;
        Registrar(Causa.PrazoVencido, $"Prazo vencido; comandos reenviados ({Reenvios}).");
    }

    public void EsgotarPrazo()
        => PararParaAcaoManual(Causa.PrazoVencido, CausaAcaoManual.PrazoEsgotado,
            $"Sem resposta em {Estado} depois de {Reenvios} reenvios.");

    // ── Auxiliares ─────────────────────────────────────────────────────────────

    private IReadOnlyList<ComandoEtapa> ComandosDeCompensacao()
    {
        var comandos = new List<ComandoEtapa>();
        if (PagamentoAprovado && !EstornoConfirmado)
            comandos.Add(ComandoEtapa.Estorno);
        if (ReservationId is not null && !LiberacaoConfirmada)
            comandos.Add(ComandoEtapa.Liberacao);
        return comandos;
    }

    // Termina a compensação quando estorno e liberação (se houver reserva) estão confirmados.
    private bool ConcluirCompensacaoOuRegistrar(Causa causa)
        => ComandosDeCompensacao().Count == 0
            ? Transitar(EstadoSaga.Compensated, causa, Motivo)
            : Registrar(causa, null);

    private bool PararParaAcaoManual(Causa causa, CausaAcaoManual motivoAcao, string motivo)
    {
        EstadoRetomada = Estado;
        CausaManual = motivoAcao;
        return Transitar(EstadoSaga.ManualActionRequired, causa, motivo);
    }

    private void LimparAcaoManual()
    {
        CausaManual = null;
        EstadoRetomada = null;
    }

    private bool EhOrcamentoVigente(Guid quoteId, int quoteVersion) => quoteId == QuoteId && quoteVersion == QuoteVersion;

    private bool EhPagamentoDaSaga(Guid paymentId) => PaymentId is null || PaymentId == paymentId;

    // Mudança de estado: zera o prazo da etapa.
    private bool Transitar(EstadoSaga novo, Causa causa, string? motivo = null)
    {
        var anterior = _transicoes.Count == 0 ? (EstadoSaga?)null : Estado;
        Estado = novo;
        Motivo = motivo ?? Motivo;
        EtapaIniciadaEm = DateTime.UtcNow;
        Reenvios = 0;
        _transicoes.Add(new TransicaoSaga(Id, anterior, novo, causa.Tipo, causa.MessageId, motivo));
        MarcarAtualizado();
        return true;
    }

    // Fato registrado sem mudar de estado (confirmação parcial, reenvio, cancelamento pedido).
    private bool Registrar(Causa causa, string? motivo)
    {
        _transicoes.Add(new TransicaoSaga(Id, Estado, Estado, causa.Tipo, causa.MessageId, motivo));
        MarcarAtualizado();
        return true;
    }
}
