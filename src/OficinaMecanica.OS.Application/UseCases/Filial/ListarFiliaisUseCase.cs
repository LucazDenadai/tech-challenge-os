using OficinaMecanica.OS.Application.Ports.Out;

namespace OficinaMecanica.OS.Application.UseCases.Filial;

public record FilialResponse(Guid Id, string Codigo, string Nome);

// Leitura das filiais ativas para a abertura de OS; o cadastro de filiais não tem escrita via API nesta entrega.
public class ListarFiliaisUseCase(IFilialRepository repository)
{
    public async Task<IEnumerable<FilialResponse>> ExecutarAsync(CancellationToken ct = default)
    {
        var filiais = await repository.ObterTodosAsync(ct);
        return filiais
            .Where(f => f.Ativo)
            .OrderBy(f => f.Codigo)
            .Select(f => new FilialResponse(f.Id, f.Codigo, f.Nome));
    }
}
