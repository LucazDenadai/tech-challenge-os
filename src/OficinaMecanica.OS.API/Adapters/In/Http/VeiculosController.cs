using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OficinaMecanica.OS.Application.UseCases.Veiculo;

namespace OficinaMecanica.OS.API.Adapters.In.Http;

[ApiController]
[Route("os/veiculos")]
[Authorize(Roles = "Admin,Atendente")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public class VeiculosController : ControllerBase
{
    private readonly GerenciarVeiculoUseCase _useCase;

    public VeiculosController(GerenciarVeiculoUseCase useCase) => _useCase = useCase;

    /// <summary>Lista todos os veículos.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<VeiculoResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Listar(CancellationToken ct)
        => Ok(await _useCase.ObterTodosAsync(ct));

    /// <summary>Obtém um veículo pelo ID.</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Admin,Atendente,Mecanico")]
    [ProducesResponseType(typeof(VeiculoResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ObterPorId(Guid id, CancellationToken ct)
    {
        var veiculo = await _useCase.ObterPorIdAsync(id, ct);
        return veiculo is null ? NotFound() : Ok(veiculo);
    }

    /// <summary>Lista veículos de um cliente pelo ID do cliente.</summary>
    [HttpGet("cliente/{clienteId:guid}")]
    [Authorize(Roles = "Admin,Atendente,Mecanico")]
    [ProducesResponseType(typeof(IEnumerable<VeiculoResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ObterPorCliente(Guid clienteId, CancellationToken ct)
        => Ok(await _useCase.ObterPorClienteAsync(clienteId, ct));

    /// <summary>Lista veículos de um cliente pelo CPF/CNPJ.</summary>
    [HttpGet("cliente/documento/{documento}")]
    [ProducesResponseType(typeof(IEnumerable<VeiculoResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ObterPorDocumentoCliente(string documento, CancellationToken ct)
        => Ok(await _useCase.ObterPorDocumentoClienteAsync(documento, ct));

    /// <summary>Cadastra um novo veículo. Placa duplicada retorna 422.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(IdResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Criar([FromBody] CriarVeiculoRequest request, CancellationToken ct)
    {
        var id = await _useCase.CriarAsync(request, ct);
        return CreatedAtAction(nameof(ObterPorId), new { id }, new IdResponse(id));
    }

    /// <summary>Atualiza marca, modelo, ano e cor de um veículo.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(VeiculoResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Atualizar(Guid id, [FromBody] AtualizarVeiculoRequest request, CancellationToken ct)
    {
        var veiculo = await _useCase.AtualizarAsync(request with { Id = id }, ct);
        return Ok(veiculo);
    }

    /// <summary>Remove um veículo. Retorna 422 se existirem OS vinculadas.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Remover(Guid id, CancellationToken ct)
    {
        await _useCase.RemoverAsync(id, ct);
        return NoContent();
    }
}
