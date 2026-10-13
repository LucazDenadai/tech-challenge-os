using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OficinaMecanica.OS.Application.Ports.Out;
using OficinaMecanica.OS.Application.UseCases.OrdemServico;
using OficinaMecanica.OS.Domain.Enums;
using OficinaMecanica.OS.Domain.Saga;
using DomainOS = OficinaMecanica.OS.Domain.Entities.OrdemServico;
using DomainCliente = OficinaMecanica.OS.Domain.Entities.Cliente;

namespace OficinaMecanica.OS.UnitTests.UseCases.OS;

public class AtualizarStatusOSUseCaseTests
{
    private readonly Mock<IOrdemServicoRepository> _repoMock = new();
    private readonly Mock<ISagaRepository> _sagaRepoMock = new();
    private readonly Mock<IEmailPort> _emailMock = new();
    private readonly Mock<IClienteRepository> _clienteRepoMock = new();
    private readonly AtualizarStatusOSUseCase _sut;

    public AtualizarStatusOSUseCaseTests()
    {
        _sut = new AtualizarStatusOSUseCase(_repoMock.Object, _sagaRepoMock.Object, _emailMock.Object, _clienteRepoMock.Object, NullLogger<AtualizarStatusOSUseCase>.Instance);
    }

    private static DomainOS CriarOS(Guid? clienteId = null)
        => new("OS-001", clienteId ?? Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Obs");

    [Fact]
    public async Task ExecutarAsync_StatusValido_AvancaStatusEPersiste()
    {
        var os = CriarOS();
        _repoMock.Setup(r => r.ObterComDetalhesAsync(os.Id, default)).ReturnsAsync(os);

        await _sut.ExecutarAsync(os.Id, StatusOrdemServico.AguardandoAprovacao);

        Assert.Equal(StatusOrdemServico.AguardandoAprovacao, os.Status);
        _repoMock.Verify(r => r.SalvarAsync(default), Times.Once);
    }

    [Fact]
    public async Task ExecutarAsync_Cancelamento_EncerraASagaAtiva()
    {
        var os = CriarOS();
        var saga = SagaOrdemServico.Iniciar(os);
        _repoMock.Setup(r => r.ObterComDetalhesAsync(os.Id, default)).ReturnsAsync(os);
        _sagaRepoMock.Setup(r => r.ObterPorOsAsync(os.Id, default)).ReturnsAsync(saga);

        await _sut.ExecutarAsync(os.Id, StatusOrdemServico.Cancelada);

        Assert.Equal(EstadoSaga.Cancelled, saga.Estado);
        Assert.Equal("CancelamentoManual", saga.Transicoes.Last().Causa);
        _repoMock.Verify(r => r.SalvarAsync(default), Times.Once);
    }

    [Fact]
    public async Task ExecutarAsync_StatusInvalido_LancaErroSemPersistirNemNotificar()
    {
        var os = CriarOS();
        _repoMock.Setup(r => r.ObterComDetalhesAsync(os.Id, default)).ReturnsAsync(os);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.ExecutarAsync(os.Id, StatusOrdemServico.Finalizada));

        _repoMock.Verify(r => r.SalvarAsync(default), Times.Never);
        _emailMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExecutarAsync_ClienteExiste_NotificaClientePorEmail()
    {
        var clienteId = Guid.NewGuid();
        var os = CriarOS(clienteId);
        var cliente = new DomainCliente("Cliente", "11144477735", "cliente@cliente.example", "11900000000", "Rua A");
        _repoMock.Setup(r => r.ObterComDetalhesAsync(os.Id, default)).ReturnsAsync(os);
        _clienteRepoMock.Setup(r => r.ObterPorIdAsync(clienteId, default)).ReturnsAsync(cliente);

        await _sut.ExecutarAsync(os.Id, StatusOrdemServico.AguardandoAprovacao);

        _emailMock.Verify(e => e.EnviarAtualizacaoStatusAsync(
            cliente.Email, os.Numero, StatusOrdemServico.AguardandoAprovacao, default), Times.Once);
    }

    [Fact]
    public async Task ExecutarAsync_FalhaNoEmail_NaoDesfazTransicao()
    {
        var clienteId = Guid.NewGuid();
        var os = CriarOS(clienteId);
        var cliente = new DomainCliente("Cliente", "11144477735", "cliente@cliente.example", "11900000000", "Rua A");
        _repoMock.Setup(r => r.ObterComDetalhesAsync(os.Id, default)).ReturnsAsync(os);
        _clienteRepoMock.Setup(r => r.ObterPorIdAsync(clienteId, default)).ReturnsAsync(cliente);
        _emailMock.Setup(e => e.EnviarAtualizacaoStatusAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StatusOrdemServico>(), default))
            .ThrowsAsync(new InvalidOperationException("SMTP indisponível"));

        await _sut.ExecutarAsync(os.Id, StatusOrdemServico.Cancelada);

        Assert.Equal(StatusOrdemServico.Cancelada, os.Status);
        _repoMock.Verify(r => r.SalvarAsync(default), Times.Once);
    }
}
