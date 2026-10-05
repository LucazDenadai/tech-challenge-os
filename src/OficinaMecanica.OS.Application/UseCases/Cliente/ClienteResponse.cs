namespace OficinaMecanica.OS.Application.UseCases.Cliente;

public record ClienteResponse(
    Guid Id,
    string Nome,
    string Documento,
    string Email,
    string Telefone,
    string Endereco,
    bool Ativo);
