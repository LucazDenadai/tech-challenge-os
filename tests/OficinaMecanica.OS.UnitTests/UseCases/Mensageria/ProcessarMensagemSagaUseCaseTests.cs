using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OficinaMecanica.OS.Application.Contratos.Saga;
using OficinaMecanica.OS.Application.Ports.Out;
using OficinaMecanica.OS.Application.UseCases.Mensageria;
using OficinaMecanica.OS.Domain.Enums;
using OficinaMecanica.OS.Domain.Saga;
using OficinaMecanica.OS.UnitTests.Contratos;
using DomainCliente = OficinaMecanica.OS.Domain.Entities.Cliente;
using DomainOS = OficinaMecanica.OS.Domain.Entities.OrdemServico;

namespace OficinaMecanica.OS.UnitTests.UseCases.Mensageria;

public class ProcessarMensagemSagaUseCaseTests
{
    private readonly Mock<IInboxRepository> _inbox = new();
    private readonly Mock<ISagaRepository> _sagas = new();
    private readonly Mock<IOrdemServicoRepository> _ordens = new();
    private readonly Mock<IClienteRepository> _clientes = new();
    private readonly Mock<IOutbox> _outbox = new();
    private readonly Mock<ITransacao> _transacao = new();
    private readonly ProcessarMensagemSagaUseCase _sut;

    private readonly DomainCliente _cliente = new("Cliente", "11144477735", "cliente@cliente.example", "11900000000", "Rua A");
    private readonly DomainOS _os;
    private readonly SagaOrdemServico _saga;

    public ProcessarMensagemSagaUseCaseTests()
    {
        _os = new DomainOS("OS-001", _cliente.Id, Guid.NewGuid(), Guid.NewGuid(), "");
        _saga = SagaOrdemServico.Iniciar(_os);

        _transacao.Setup(t => t.ExecutarAsync(It.IsAny<Func<Task<StatusRecebimento>>>(), default))
            .Returns<Func<Task<StatusRecebimento>>, CancellationToken>((acao, _) => acao());
        _inbox.Setup(i => i.RegistrarAsync(It.IsAny<MensagemSaga>(), It.IsAny<string>(), It.IsAny<string>(), default)).ReturnsAsync(true);
        _sagas.Setup(s => s.ObterPorOsAsync(_os.Id, default)).ReturnsAsync(_saga);
        _ordens.Setup(o => o.ObterPorIdAsync(_os.Id, default)).ReturnsAsync(_os);
        _clientes.Setup(c => c.ObterPorIdAsync(_cliente.Id, default)).ReturnsAsync(_cliente);

        _sut = new ProcessarMensagemSagaUseCase(_transacao.Object, _inbox.Object, _sagas.Object, _ordens.Object,
            _clientes.Object, _outbox.Object, NullLogger<ProcessarMensagemSagaUseCase>.Instance);
    }

    // Exemplo válido do canal, endereçado à Saga do teste.
    private JsonObject Mensagem(string canal, Action<JsonObject>? ajustar = null)
    {
        var mensagem = MensagensExemplo.Criar(CatalogoCanaisOS.Obter(canal)!);
        mensagem["osId"] = _os.Id.ToString();
        mensagem["filialId"] = _os.FilialId.ToString();
        mensagem["correlationId"] = _saga.Id.ToString();
        ajustar?.Invoke(mensagem);
        return mensagem;
    }

    private Task<ResultadoRecebimento> Processar(string canal, JsonObject mensagem)
        => _sut.ExecutarAsync(canal, MensagensExemplo.Bytes(mensagem));

    [Fact]
    public async Task CanalQueOOSNaoConsome_Rejeita()
    {
        var resultado = await _sut.ExecutarAsync("saga-os.diagnosis-requested.v1",
            MensagensExemplo.Bytes(MensagensExemplo.Criar(CatalogoCanaisOS.Todos[0])));

        Assert.Equal(StatusRecebimento.Rejeitada, resultado.Status);
        Assert.Contains("não é consumido pelo OS", resultado.Motivo);
        _inbox.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PayloadForaDoContrato_RejeitaSemTocarNaInbox()
    {
        var mensagem = Mensagem("saga-os.payment-approved.v1", m => m.Remove("paymentId"));

        var resultado = await Processar("saga-os.payment-approved.v1", mensagem);

        Assert.Equal(StatusRecebimento.Rejeitada, resultado.Status);
        _inbox.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MessageIdJaRegistrado_DuplicadaSemTocarNaSaga()
    {
        _inbox.Setup(i => i.RegistrarAsync(It.IsAny<MensagemSaga>(), It.IsAny<string>(), It.IsAny<string>(), default)).ReturnsAsync(false);

        var resultado = await Processar("saga-os.diagnosis-completed.v1", Mensagem("saga-os.diagnosis-completed.v1"));

        Assert.Equal(StatusRecebimento.Duplicada, resultado.Status);
        _sagas.VerifyNoOtherCalls();
        _outbox.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task OsSemSaga_RejeitaComMotivo()
    {
        var resultado = await Processar("saga-os.diagnosis-completed.v1",
            Mensagem("saga-os.diagnosis-completed.v1", m => m["osId"] = Guid.NewGuid().ToString()));

        Assert.Equal(StatusRecebimento.Rejeitada, resultado.Status);
        Assert.Contains("Saga da OS", resultado.Motivo);
        _outbox.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CorrelationIdDeOutraSaga_Rejeita()
    {
        var resultado = await Processar("saga-os.diagnosis-completed.v1",
            Mensagem("saga-os.diagnosis-completed.v1", m => m["correlationId"] = Guid.NewGuid().ToString()));

        Assert.Equal(StatusRecebimento.Rejeitada, resultado.Status);
        Assert.Equal(EstadoSaga.Diagnosing, _saga.Estado);
    }

    [Fact]
    public async Task DiagnosticoConcluido_PedeOrcamentoEAtualizaStatusDaOS()
    {
        var mensagem = Mensagem("saga-os.diagnosis-completed.v1");

        var resultado = await Processar("saga-os.diagnosis-completed.v1", mensagem);

        Assert.Equal(StatusRecebimento.Aplicada, resultado.Status);
        Assert.Equal(EstadoSaga.RequestingQuote, _saga.Estado);
        Assert.Equal(StatusOrdemServico.AguardandoAprovacao, _os.Status);
        _outbox.Verify(o => o.Adicionar(It.Is<QuoteRequested>(q =>
            q.CausationId == Guid.Parse(mensagem["messageId"]!.GetValue<string>())
            && q.ClienteId == _cliente.Id && q.Items.Count == 1)), Times.Once);
        _ordens.Verify(o => o.AtualizarAsync(_os, default), Times.Once);
        _ordens.Verify(o => o.SalvarAsync(default), Times.Once);
    }

    [Fact]
    public async Task ReservaConfirmada_PedePagamentoComEmailDoCliente()
    {
        var quoteId = LevarAte(EstadoSaga.ReservingInventory);

        var resultado = await Processar("saga-os.inventory-reserved.v1", Mensagem("saga-os.inventory-reserved.v1"));

        Assert.Equal(StatusRecebimento.Aplicada, resultado.Status);
        _outbox.Verify(o => o.Adicionar(It.Is<PaymentCreationRequested>(p =>
            p.PayerEmail == "cliente@cliente.example" && p.QuoteId == quoteId && p.Amount == 100m)), Times.Once);
    }

    [Fact]
    public async Task MensagemForaDoEstado_ForaDoFluxoMasInboxGravada()
    {
        // QuoteApproved com a Saga ainda em Diagnosing.
        var resultado = await Processar("saga-os.quote-approved.v1", Mensagem("saga-os.quote-approved.v1"));

        Assert.Equal(StatusRecebimento.ForaDoFluxo, resultado.Status);
        Assert.Equal(EstadoSaga.Diagnosing, _saga.Estado);
        _outbox.VerifyNoOtherCalls();
        _ordens.Verify(o => o.SalvarAsync(default), Times.Once);
    }

    [Theory]
    [InlineData("saga-os.payment-declined.v1")]
    [InlineData("saga-os.execution-failed.v1")]
    [InlineData("saga-os.payment-refunded.v1")]
    public async Task EventoDoCard40b_ForaDoFluxo(string canal)
    {
        LevarAte(EstadoSaga.AwaitingPayment);

        var resultado = await Processar(canal, Mensagem(canal));

        Assert.Equal(StatusRecebimento.ForaDoFluxo, resultado.Status);
        Assert.Equal(EstadoSaga.AwaitingPayment, _saga.Estado);
    }

    // Avança a Saga direto pelo domínio; devolve o quoteId do orçamento registrado.
    // Os estados usados aqui ficam a um passo de status por vez, então uma sincronização basta por etapa.
    private Guid LevarAte(EstadoSaga estado)
    {
        var quoteId = Guid.NewGuid();
        var c = new Causa("Teste", Guid.NewGuid());
        _saga.RegistrarDiagnostico(c, Guid.NewGuid(), "BRL", DateTimeOffset.UtcNow,
            [new ItemDiagnostico(Guid.NewGuid(), TipoItemDiagnostico.Peca, "Filtro", 1, 100m)]);
        StatusDaSaga.Sincronizar(_os, _saga);
        _saga.RegistrarOrcamento(c, quoteId, 1, 100m, "BRL");
        _saga.AprovarOrcamento(c, quoteId, 1);
        if (estado != EstadoSaga.ReservingInventory)
        {
            _saga.RegistrarReserva(c, Guid.NewGuid());
            _saga.RegistrarPagamentoPendente(c, Guid.NewGuid());
        }

        // Como no processamento real, o status da OS acompanha cada mudança da Saga.
        StatusDaSaga.Sincronizar(_os, _saga);
        Assert.Equal(estado, _saga.Estado);
        return quoteId;
    }
}
