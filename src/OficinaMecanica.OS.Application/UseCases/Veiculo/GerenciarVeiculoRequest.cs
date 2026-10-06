namespace OficinaMecanica.OS.Application.UseCases.Veiculo;

public record CriarVeiculoRequest(Guid ClienteId, string Placa, string Marca, string Modelo, int Ano, string Cor);
public record AtualizarVeiculoRequest(Guid Id, string Marca, string Modelo, int Ano, string Cor);
