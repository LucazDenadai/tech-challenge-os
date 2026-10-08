using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OficinaMecanica.OS.Application.UseCases.OrdemServico;

namespace OficinaMecanica.OS.API.Adapters.In.Http;

[ApiController]
[Route("os/ordens-servico")]
[Authorize(Roles = "Cliente")]
public class AcompanhamentoOSController : ControllerBase
{
    // Claim emitida pela Lambda de autenticação por CPF (RFC-003).
    public const string ClaimClienteId = "cliente_id";

    private readonly AcompanharOSUseCase _acompanharUseCase;

    public AcompanhamentoOSController(AcompanharOSUseCase acompanharUseCase) => _acompanharUseCase = acompanharUseCase;

    /// <summary>Acompanhamento da OS pelo número, pelo próprio cliente (JWT emitido pela Lambda de CPF).</summary>
    /// <remarks>Sem IDs internos: só número, status, datas e histórico. OS de outro cliente responde 404.</remarks>
    [HttpGet("acompanhar/{numero}")]
    [ProducesResponseType(typeof(AcompanhamentoOSResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Acompanhar(string numero, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirst(ClaimClienteId)?.Value, out var clienteId))
            return Forbid();

        var os = await _acompanharUseCase.ExecutarAsync(numero, clienteId, ct);
        return os is null ? NotFound() : Ok(os);
    }
}
