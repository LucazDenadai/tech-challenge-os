using System.Diagnostics.CodeAnalysis;
using OficinaMecanica.OS.Domain.Validators;

namespace OficinaMecanica.OS.Domain.Entities;

public class Veiculo : EntityBase
{
    public Guid ClienteId { get; private set; }
    public string Placa { get; private set; } = string.Empty;
    public string Marca { get; private set; } = string.Empty;
    public string Modelo { get; private set; } = string.Empty;
    public int Ano { get; private set; }
    public string Cor { get; private set; } = string.Empty;

    public Cliente? Cliente { get; protected set; }

    [ExcludeFromCodeCoverage]
    protected Veiculo() { }

    public Veiculo(Guid clienteId, string placa, string marca, string modelo, int ano, string cor)
    {
        PlacaValidator.Validar(placa);
        ClienteId = clienteId;
        Placa = PlacaValidator.Normalizar(placa);
        Marca = marca;
        Modelo = modelo;
        Ano = ano;
        Cor = cor;
    }

    public void Atualizar(string marca, string modelo, int ano, string cor)
    {
        Marca = marca;
        Modelo = modelo;
        Ano = ano;
        Cor = cor;
        MarcarAtualizado();
    }
}
