namespace OficinaMecanica.OS.Application.UseCases.Veiculo;

public record VeiculoResponse(
    Guid Id,
    Guid ClienteId,
    string Placa,
    string Marca,
    string Modelo,
    int Ano,
    string Cor);
