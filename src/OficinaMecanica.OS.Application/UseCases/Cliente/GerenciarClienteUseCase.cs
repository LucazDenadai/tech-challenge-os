using OficinaMecanica.OS.Application.Exceptions;
using OficinaMecanica.OS.Application.Ports.Out;
using DomainCliente = OficinaMecanica.OS.Domain.Entities.Cliente;

namespace OficinaMecanica.OS.Application.UseCases.Cliente;

public class GerenciarClienteUseCase(IClienteRepository repository)
{
    public async Task<IEnumerable<ClienteResponse>> ObterTodosAsync(CancellationToken ct = default)
    {
        var clientes = await repository.ObterTodosAsync(ct);
        return clientes.Where(c => c.Ativo).Select(ToResponse);
    }

    public async Task<ClienteResponse?> ObterPorIdAsync(Guid id, CancellationToken ct = default)
    {
        var cliente = await repository.ObterPorIdAsync(id, ct);
        return cliente is null ? null : ToResponse(cliente);
    }

    public async Task<IEnumerable<ClienteResponse>> BuscarAsync(string termo, CancellationToken ct = default)
    {
        var clientes = await repository.BuscarAsync(termo, ct);
        return clientes.Select(ToResponse);
    }

    public async Task<Guid> CriarAsync(CriarClienteRequest request, CancellationToken ct = default)
    {
        var documentoSanitizado = DomainCliente.Sanitizar(request.Documento);

        var existe = await repository.DocumentoExisteAsync(documentoSanitizado, ct: ct);
        if (existe)
        {
            var existente = await repository.ObterPorDocumentoAsync(documentoSanitizado, ct);
            if (existente is not null && !existente.Ativo)
            {
                existente.Ativar(request.Nome, request.Email, request.Telefone, request.Endereco);
                await repository.AtualizarAsync(existente, ct);
                await repository.SalvarAsync(ct);
                return existente.Id;
            }
            throw new InvalidOperationException($"Já existe um cliente ativo com o documento informado.");
        }

        var cliente = new DomainCliente(request.Nome, request.Documento, request.Email, request.Telefone, request.Endereco);
        await repository.AdicionarAsync(cliente, ct);
        await repository.SalvarAsync(ct);
        return cliente.Id;
    }

    public async Task<ClienteResponse> AtualizarAsync(AtualizarClienteRequest request, CancellationToken ct = default)
    {
        var cliente = await repository.ObterPorIdAsync(request.Id, ct)
            ?? throw new NotFoundException("Cliente", request.Id);

        cliente.Atualizar(request.Nome, request.Email, request.Telefone, request.Endereco);
        await repository.AtualizarAsync(cliente, ct);
        await repository.SalvarAsync(ct);
        return ToResponse(cliente);
    }

    public async Task DesativarAsync(Guid id, CancellationToken ct = default)
    {
        var cliente = await repository.ObterPorIdAsync(id, ct)
            ?? throw new NotFoundException("Cliente", id);

        cliente.Desativar();
        await repository.AtualizarAsync(cliente, ct);
        await repository.SalvarAsync(ct);
    }

    private static ClienteResponse ToResponse(DomainCliente c) =>
        new(c.Id, c.Nome, c.Documento, c.Email, c.Telefone, c.Endereco, c.Ativo);
}
