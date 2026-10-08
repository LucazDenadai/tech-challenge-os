using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OficinaMecanica.OS.Application.UseCases.Filial;

namespace OficinaMecanica.OS.API.Adapters.In.Http;

[ApiController]
[Route("os/filiais")]
[Authorize(Roles = "Admin,Atendente,Mecanico")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public class FiliaisController : ControllerBase
{
    private readonly ListarFiliaisUseCase _useCase;

    public FiliaisController(ListarFiliaisUseCase useCase) => _useCase = useCase;

    /// <summary>Lista as filiais ativas, para informar o filialId na abertura da OS.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<FilialResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Listar(CancellationToken ct)
        => Ok(await _useCase.ExecutarAsync(ct));
}
