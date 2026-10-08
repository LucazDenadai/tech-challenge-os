using OficinaMecanica.OS.Application.Validators;

namespace OficinaMecanica.OS.Application.UseCases.Veiculo;

public record CriarVeiculoRequest([IdObrigatorio] Guid ClienteId, string Placa, string Marca, string Modelo, int Ano, string Cor);
public record AtualizarVeiculoRequest(Guid Id, string Marca, string Modelo, int Ano, string Cor);
