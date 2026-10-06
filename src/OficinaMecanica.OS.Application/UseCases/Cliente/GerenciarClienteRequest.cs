namespace OficinaMecanica.OS.Application.UseCases.Cliente;

public record CriarClienteRequest(string Nome, string Documento, string Email, string Telefone, string Endereco);
public record AtualizarClienteRequest(Guid Id, string Nome, string Email, string Telefone, string Endereco);
