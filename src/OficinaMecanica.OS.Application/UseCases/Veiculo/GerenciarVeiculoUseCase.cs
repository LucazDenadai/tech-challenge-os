using OficinaMecanica.OS.Application.Exceptions;
using OficinaMecanica.OS.Application.Ports.Out;
using OficinaMecanica.OS.Domain.Validators;
using DomainVeiculo = OficinaMecanica.OS.Domain.Entities.Veiculo;

namespace OficinaMecanica.OS.Application.UseCases.Veiculo;

public class GerenciarVeiculoUseCase(IVeiculoRepository veiculoRepository, IClienteRepository clienteRepository)
{
    public async Task<IEnumerable<VeiculoResponse>> ObterTodosAsync(CancellationToken ct = default)
    {
        var veiculos = await veiculoRepository.ObterTodosAsync(ct);
        return veiculos.Select(ToResponse);
    }

    public async Task<VeiculoResponse?> ObterPorIdAsync(Guid id, CancellationToken ct = default)
    {
        var veiculo = await veiculoRepository.ObterPorIdAsync(id, ct);
        return veiculo is null ? null : ToResponse(veiculo);
    }

    public async Task<IEnumerable<VeiculoResponse>> ObterPorClienteAsync(Guid clienteId, CancellationToken ct = default)
    {
        var veiculos = await veiculoRepository.ObterPorClienteAsync(clienteId, ct);
        return veiculos.Select(ToResponse);
    }

    public async Task<IEnumerable<VeiculoResponse>> ObterPorDocumentoClienteAsync(string documento, CancellationToken ct = default)
    {
        var veiculos = await veiculoRepository.ObterPorDocumentoClienteAsync(documento, ct);
        return veiculos.Select(ToResponse);
    }

    public async Task RemoverAsync(Guid id, CancellationToken ct = default)
    {
        var veiculo = await veiculoRepository.ObterPorIdAsync(id, ct)
            ?? throw new NotFoundException("Veiculo", id);

        var possuiOS = await veiculoRepository.PossuiOrdensServicoAsync(id, ct);
        if (possuiOS)
            throw new InvalidOperationException("Não é possível remover o veículo pois existem ordens de serviço associadas.");

        await veiculoRepository.RemoverAsync(veiculo, ct);
        await veiculoRepository.SalvarAsync(ct);
    }

    public async Task<Guid> CriarAsync(CriarVeiculoRequest request, CancellationToken ct = default)
    {
        var clienteExiste = await clienteRepository.ObterPorIdAsync(request.ClienteId, ct);
        if (clienteExiste is null)
            throw new NotFoundException("Cliente", request.ClienteId);

        var placaNormalizada = PlacaValidator.Normalizar(request.Placa);
        var placaDuplicada = await veiculoRepository.ObterPorPlacaAsync(placaNormalizada, ct);
        if (placaDuplicada is not null)
            throw new InvalidOperationException($"Já existe um veículo com a placa '{placaNormalizada}'.");

        var veiculo = new DomainVeiculo(request.ClienteId, request.Placa, request.Marca, request.Modelo, request.Ano, request.Cor);
        await veiculoRepository.AdicionarAsync(veiculo, ct);
        await veiculoRepository.SalvarAsync(ct);
        return veiculo.Id;
    }

    public async Task<VeiculoResponse> AtualizarAsync(AtualizarVeiculoRequest request, CancellationToken ct = default)
    {
        var veiculo = await veiculoRepository.ObterPorIdAsync(request.Id, ct)
            ?? throw new NotFoundException("Veiculo", request.Id);

        veiculo.Atualizar(request.Marca, request.Modelo, request.Ano, request.Cor);
        await veiculoRepository.AtualizarAsync(veiculo, ct);
        await veiculoRepository.SalvarAsync(ct);
        return ToResponse(veiculo);
    }

    private static VeiculoResponse ToResponse(DomainVeiculo v) =>
        new(v.Id, v.ClienteId, v.Placa, v.Marca, v.Modelo, v.Ano, v.Cor);
}
