using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OficinaMecanica.OS.Application.UseCases.Auth;

namespace OficinaMecanica.OS.API.Adapters.In.Http;

[ApiController]
[Route("os/auth")]
public class AuthController : ControllerBase
{
    private readonly AuthUseCase _useCase;

    public AuthController(AuthUseCase useCase) => _useCase = useCase;

    /// <summary>Login de funcionário (Admin, Atendente, Mecânico). Retorna o JWT aceito por OS, Billing e Operações.</summary>
    /// <response code="200">Credenciais válidas; corpo traz o token.</response>
    /// <response code="401">E-mail inexistente, usuário desativado ou senha incorreta (mesma resposta para os três).</response>
    /// <response code="429">Mais de 10 tentativas por minuto do mesmo IP.</response>
    [HttpPost("login")]
    [EnableRateLimiting("login")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var token = await _useCase.LoginAsync(request, ct);
        return Ok(new LoginResponse(token));
    }
}

public record LoginResponse(string Token);
