using OficinaMecanica.OS.Application.Ports.Out;
using OficinaMecanica.OS.Domain.Validators;
using DomainCliente = OficinaMecanica.OS.Domain.Entities.Cliente;

namespace OficinaMecanica.OS.Application.UseCases.Cliente;

public record BuscarClientePorCpfRequest(string Cpf);

// Somente o necessário para a Lambda emitir o JWT do cliente (ADR-015): sem nome, e-mail ou documento.
public record ClienteAutenticacaoResponse(Guid ClienteId, bool Ativo);

public class BuscarClientePorCpfUseCase(IClienteRepository repository)
{
    public async Task<ClienteAutenticacaoResponse?> ExecutarAsync(string cpf, CancellationToken ct = default)
    {
        CpfValidator.Validar(cpf);

        var cliente = await repository.ObterPorDocumentoAsync(DomainCliente.Sanitizar(cpf), ct);
        return cliente is null ? null : new ClienteAutenticacaoResponse(cliente.Id, cliente.Ativo);
    }
}
