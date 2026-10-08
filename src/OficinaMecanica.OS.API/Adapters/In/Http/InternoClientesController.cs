using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OficinaMecanica.OS.API.Auth;
using OficinaMecanica.OS.Application.UseCases.Cliente;

namespace OficinaMecanica.OS.API.Adapters.In.Http;

// Endpoint interno para a Lambda de CPF, que deixa de consultar o banco do OS (ADR-015).
// Não deve ser roteado pelo API Gateway público; só a Lambda conhece a chave.
[ApiController]
[Route("os/interno/clientes")]
[Authorize(AuthenticationSchemes = ApiKeyInternaHandler.Esquema)]
public class InternoClientesController : ControllerBase
{
    private readonly BuscarClientePorCpfUseCase _useCase;

    public InternoClientesController(BuscarClientePorCpfUseCase useCase) => _useCase = useCase;

    /// <summary>Localiza o cliente pelo CPF e devolve só o ID e se está ativo.</summary>
    /// <remarks>CPF vai no corpo, não na URL, para não aparecer em logs de acesso.</remarks>
    /// <response code="200">Cliente encontrado (ativo ou não; a Lambda decide o 403).</response>
    /// <response code="400">CPF com formato inválido.</response>
    /// <response code="401">Cabeçalho X-Api-Key ausente ou inválido.</response>
    /// <response code="404">Nenhum cliente com o CPF.</response>
    [HttpPost("busca-por-cpf")]
    [ProducesResponseType(typeof(ClienteAutenticacaoResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> BuscarPorCpf([FromBody] BuscarClientePorCpfRequest request, CancellationToken ct)
    {
        var cliente = await _useCase.ExecutarAsync(request.Cpf, ct);
        return cliente is null ? NotFound() : Ok(cliente);
    }
}
