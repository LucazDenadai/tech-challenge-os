using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OficinaMecanica.OS.Application.Contratos.Saga;
using OficinaMecanica.OS.Application.Ports.Out;
using OficinaMecanica.OS.Application.UseCases.Mensageria;
using OficinaMecanica.OS.UnitTests.Contratos;

namespace OficinaMecanica.OS.UnitTests.UseCases.Mensageria;

public class RegistrarMensagemRecebidaUseCaseTests
{
    private readonly Mock<IInboxRepository> _inboxMock = new();
    private readonly RegistrarMensagemRecebidaUseCase _sut;
    private static readonly CanalConsumido _canal = CatalogoCanaisOS.Obter("saga-os.payment-approved.v1")!;

    public RegistrarMensagemRecebidaUseCaseTests()
    {
        _sut = new RegistrarMensagemRecebidaUseCase(_inboxMock.Object, NullLogger<RegistrarMensagemRecebidaUseCase>.Instance);
    }

    [Fact]
    public async Task ExecutarAsync_MensagemNova_RegistraNaInbox()
    {
        var exemplo = MensagensExemplo.Criar(_canal);
        _inboxMock.Setup(i => i.RegistrarAsync(It.IsAny<MensagemSaga>(), _canal.Endereco, It.IsAny<string>(), default)).ReturnsAsync(true);

        var resultado = await _sut.ExecutarAsync(_canal.Endereco, MensagensExemplo.Bytes(exemplo));

        Assert.Equal(StatusRecebimento.Registrada, resultado.Status);
        Assert.Equal(Guid.Parse(exemplo["correlationId"]!.GetValue<string>()), resultado.CorrelationId);
        _inboxMock.Verify(i => i.RegistrarAsync(
            It.Is<MensagemSaga>(m => m is PaymentApproved && m.MessageId == Guid.Parse(exemplo["messageId"]!.GetValue<string>())),
            _canal.Endereco, exemplo.ToJsonString(), default), Times.Once);
    }

    [Fact]
    public async Task ExecutarAsync_MessageIdJaRegistrado_DevolveDuplicada()
    {
        _inboxMock.Setup(i => i.RegistrarAsync(It.IsAny<MensagemSaga>(), _canal.Endereco, It.IsAny<string>(), default)).ReturnsAsync(false);

        var resultado = await _sut.ExecutarAsync(_canal.Endereco, MensagensExemplo.Bytes(MensagensExemplo.Criar(_canal)));

        Assert.Equal(StatusRecebimento.Duplicada, resultado.Status);
        Assert.Null(resultado.Motivo);
    }

    [Fact]
    public async Task ExecutarAsync_PayloadForaDoContrato_RejeitaSemTocarNaInbox()
    {
        var exemplo = MensagensExemplo.Criar(_canal);
        exemplo.Remove("paymentId");

        var resultado = await _sut.ExecutarAsync(_canal.Endereco, MensagensExemplo.Bytes(exemplo));

        Assert.Equal(StatusRecebimento.Rejeitada, resultado.Status);
        Assert.NotNull(resultado.Motivo);
        _inboxMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExecutarAsync_CanalQueOOSNaoConsome_Rejeita()
    {
        // DiagnosisRequested é publicado pelo OS, não consumido por ele.
        var resultado = await _sut.ExecutarAsync("saga-os.diagnosis-requested.v1", MensagensExemplo.Bytes(MensagensExemplo.Criar(_canal)));

        Assert.Equal(StatusRecebimento.Rejeitada, resultado.Status);
        Assert.Contains("não é consumido pelo OS", resultado.Motivo);
        _inboxMock.VerifyNoOtherCalls();
    }
}
