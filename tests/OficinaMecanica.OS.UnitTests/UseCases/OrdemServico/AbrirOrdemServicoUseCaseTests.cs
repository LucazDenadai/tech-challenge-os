using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OficinaMecanica.OS.Application.Contratos.Saga;
using OficinaMecanica.OS.Application.Exceptions;
using OficinaMecanica.OS.Application.Ports.Out;
using OficinaMecanica.OS.Application.UseCases.OrdemServico;
using OficinaMecanica.OS.Domain.Entities;
using OficinaMecanica.OS.Domain.Enums;
using OficinaMecanica.OS.Domain.Saga;
using DomainCliente = OficinaMecanica.OS.Domain.Entities.Cliente;
using DomainOS = OficinaMecanica.OS.Domain.Entities.OrdemServico;
using DomainVeiculo = OficinaMecanica.OS.Domain.Entities.Veiculo;

namespace OficinaMecanica.OS.UnitTests.UseCases.OS;

public class AbrirOrdemServicoUseCaseTests
{
    private readonly Mock<IOrdemServicoRepository> _osRepoMock = new();
    private readonly Mock<IVeiculoRepository> _veiculoRepoMock = new();
    private readonly Mock<IFilialRepository> _filialRepoMock = new();
    private readonly Mock<IClienteRepository> _clienteRepoMock = new();
    private readonly Mock<ISagaRepository> _sagaRepoMock = new();
    private readonly Mock<IOutbox> _outboxMock = new();
    private readonly AbrirOrdemServicoUseCase _sut;

    private readonly Guid _clienteId = Guid.NewGuid();
    private readonly Guid _veiculoId = Guid.NewGuid();
    private readonly Filial _filial = new("FILIAL-SP", "Filial SP");

    public AbrirOrdemServicoUseCaseTests()
    {
        _sut = new AbrirOrdemServicoUseCase(_osRepoMock.Object, _veiculoRepoMock.Object, _filialRepoMock.Object,
            _clienteRepoMock.Object, _sagaRepoMock.Object, _outboxMock.Object, NullLogger<AbrirOrdemServicoUseCase>.Instance);
        _clienteRepoMock.Setup(r => r.ObterPorIdAsync(_clienteId, default))
            .ReturnsAsync(new DomainCliente("Cliente", "11144477735", "cliente@cliente.example", "11900000000", "Rua A"));
        _veiculoRepoMock.Setup(r => r.ObterPorIdAsync(_veiculoId, default))
            .ReturnsAsync(new DomainVeiculo(_clienteId, "ABC1D23", "Ford", "Fiesta", 2020, "Prata"));
        _filialRepoMock.Setup(r => r.ObterPorIdAsync(_filial.Id, default)).ReturnsAsync(_filial);
        _osRepoMock.Setup(r => r.GerarNumeroAsync(default)).ReturnsAsync("OS-001");
    }

    [Fact]
    public async Task ExecutarAsync_DadosValidos_CriaOSEmDiagnosticoNaFilial()
    {
        var result = await _sut.ExecutarAsync(new AbrirOrdemServicoRequest(_clienteId, _veiculoId, _filial.Id, "Revisão"));

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("OS-001", result.Numero);
        _osRepoMock.Verify(r => r.AdicionarAsync(
            It.Is<DomainOS>(os => os.Status == StatusOrdemServico.EmDiagnostico && os.FilialId == _filial.Id),
            default), Times.Once);
        _osRepoMock.Verify(r => r.SalvarAsync(default), Times.Once);
    }

    [Fact]
    public async Task ExecutarAsync_DadosValidos_IniciaSagaEPublicaDiagnosisRequestedNaMesmaGravacao()
    {
        SagaOrdemServico? saga = null;
        _sagaRepoMock.Setup(r => r.AdicionarAsync(It.IsAny<SagaOrdemServico>(), default)).Callback<SagaOrdemServico, CancellationToken>((s, _) => saga = s);

        var result = await _sut.ExecutarAsync(new AbrirOrdemServicoRequest(_clienteId, _veiculoId, _filial.Id, "Revisão"));

        Assert.NotNull(saga);
        Assert.Equal(result.Id, saga.OsId);
        Assert.Equal(EstadoSaga.Diagnosing, saga.Estado);
        _outboxMock.Verify(o => o.Adicionar(It.Is<DiagnosisRequested>(d =>
            d.CorrelationId == saga.Id && d.OsId == result.Id && d.VeiculoId == _veiculoId && d.CausationId == null)), Times.Once);
        _osRepoMock.Verify(r => r.SalvarAsync(default), Times.Once);
    }

    [Fact]
    public async Task ExecutarAsync_ClienteSemEmailValido_LancaInvalidOperationExceptionSemPersistir()
    {
        // Cadastro antigo, gravado antes da validação de e-mail.
        var cliente = new DomainCliente("Cliente", "11144477735", "cliente@cliente.example", "11900000000", "Rua A");
        typeof(DomainCliente).GetProperty(nameof(DomainCliente.Email))!.SetValue(cliente, "");
        _clienteRepoMock.Setup(r => r.ObterPorIdAsync(_clienteId, default)).ReturnsAsync(cliente);

        var erro = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.ExecutarAsync(new AbrirOrdemServicoRequest(_clienteId, _veiculoId, _filial.Id, "Obs")));

        Assert.Contains("e-mail", erro.Message);
        _osRepoMock.Verify(r => r.SalvarAsync(default), Times.Never);
        _outboxMock.Verify(o => o.Adicionar(It.IsAny<MensagemSaga>()), Times.Never);
    }

    [Fact]
    public async Task ExecutarAsync_VeiculoDeOutroCliente_LancaInvalidOperationException()
    {
        var request = new AbrirOrdemServicoRequest(Guid.NewGuid(), _veiculoId, _filial.Id, "Obs");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.ExecutarAsync(request));
        _osRepoMock.Verify(r => r.SalvarAsync(default), Times.Never);
    }

    [Fact]
    public async Task ExecutarAsync_FilialInexistente_LancaNotFoundExceptionSemPersistir()
    {
        var request = new AbrirOrdemServicoRequest(_clienteId, _veiculoId, Guid.NewGuid(), "Obs");

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.ExecutarAsync(request));
        _osRepoMock.Verify(r => r.SalvarAsync(default), Times.Never);
    }

    [Fact]
    public async Task ExecutarAsync_FilialInativa_LancaInvalidOperationExceptionSemPersistir()
    {
        _filial.Desativar();
        var request = new AbrirOrdemServicoRequest(_clienteId, _veiculoId, _filial.Id, "Obs");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.ExecutarAsync(request));
        _osRepoMock.Verify(r => r.SalvarAsync(default), Times.Never);
    }
}
