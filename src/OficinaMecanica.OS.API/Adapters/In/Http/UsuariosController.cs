using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OficinaMecanica.OS.Application.UseCases.Usuario;

namespace OficinaMecanica.OS.API.Adapters.In.Http;

[ApiController]
[Route("os/usuarios")]
[Authorize(Roles = "Admin")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public class UsuariosController : ControllerBase
{
    private readonly GerenciarUsuarioUseCase _useCase;

    public UsuariosController(GerenciarUsuarioUseCase useCase) => _useCase = useCase;

    /// <summary>Lista usuários funcionários ativos. Aceita ?email= para filtrar.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<UsuarioResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Listar([FromQuery] string? email, CancellationToken ct)
    {
        var resultado = email is not null
            ? await _useCase.BuscarPorEmailAsync(email, ct)
            : await _useCase.ObterTodosAsync(ct);
        return Ok(resultado);
    }

    /// <summary>Obtém usuário por ID.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(UsuarioResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ObterPorId(Guid id, CancellationToken ct)
    {
        var usuario = await _useCase.ObterPorIdAsync(id, ct);
        return usuario is null ? NotFound() : Ok(usuario);
    }

    /// <summary>Cria usuário funcionário. E-mail já cadastrado retorna 422.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(UsuarioResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Criar([FromBody] CriarUsuarioRequest request, CancellationToken ct)
    {
        var usuario = await _useCase.CriarAsync(request, ct);
        return CreatedAtAction(nameof(ObterPorId), new { id = usuario.Id }, usuario);
    }

    /// <summary>Atualiza nome, e-mail, perfil e, opcionalmente, a senha.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(UsuarioResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Atualizar(Guid id, [FromBody] AtualizarUsuarioRequest request, CancellationToken ct)
    {
        var usuario = await _useCase.AtualizarAsync(id, request, ct);
        return Ok(usuario);
    }

    /// <summary>Desativa um usuário (soft delete); ele deixa de conseguir fazer login.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Desativar(Guid id, CancellationToken ct)
    {
        await _useCase.DesativarAsync(id, ct);
        return NoContent();
    }
}
