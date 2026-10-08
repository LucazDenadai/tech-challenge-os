using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OficinaMecanica.OS.Application.UseCases.Cliente;

namespace OficinaMecanica.OS.API.Adapters.In.Http;

[ApiController]
[Route("os/clientes")]
[Authorize(Roles = "Admin,Atendente")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public class ClientesController : ControllerBase
{
    private readonly GerenciarClienteUseCase _useCase;

    public ClientesController(GerenciarClienteUseCase useCase) => _useCase = useCase;

    /// <summary>Lista clientes ativos. Aceita ?busca= para filtrar por nome, CPF/CNPJ ou e-mail.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<ClienteResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Listar([FromQuery] string? busca, CancellationToken ct)
    {
        var resultado = busca is not null
            ? await _useCase.BuscarAsync(busca, ct)
            : await _useCase.ObterTodosAsync(ct);
        return Ok(resultado);
    }

    /// <summary>Cria um novo cliente. Documento inválido retorna 400; documento já ativo retorna 422.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(IdResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Criar([FromBody] CriarClienteRequest request, CancellationToken ct)
    {
        var id = await _useCase.CriarAsync(request, ct);
        return CreatedAtAction(nameof(ObterPorId), new { id }, new IdResponse(id));
    }

    /// <summary>Obtém um cliente pelo ID.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ClienteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ObterPorId(Guid id, CancellationToken ct)
    {
        var cliente = await _useCase.ObterPorIdAsync(id, ct);
        return cliente is null ? NotFound() : Ok(cliente);
    }

    /// <summary>Atualiza nome, e-mail, telefone e endereço do cliente. Documento não é alterável.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ClienteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Atualizar(Guid id, [FromBody] AtualizarClienteRequest request, CancellationToken ct)
    {
        var cliente = await _useCase.AtualizarAsync(request with { Id = id }, ct);
        return Ok(cliente);
    }

    /// <summary>Desativa um cliente.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Desativar(Guid id, CancellationToken ct)
    {
        await _useCase.DesativarAsync(id, ct);
        return NoContent();
    }
}

public record IdResponse(Guid Id);
