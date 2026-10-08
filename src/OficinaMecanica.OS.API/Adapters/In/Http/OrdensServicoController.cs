using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OficinaMecanica.OS.Application.UseCases.OrdemServico;
using OficinaMecanica.OS.Domain.Enums;

namespace OficinaMecanica.OS.API.Adapters.In.Http;

// O status da OS só muda pela Saga (ADR-017, CARD-40). Por API ficam apenas as transições manuais:
// entrega (Finalizada → Entregue) e cancelamento.
[ApiController]
[Route("os/ordens-servico")]
[Authorize(Roles = "Admin,Atendente,Mecanico")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public class OrdensServicoController : ControllerBase
{
    private readonly AbrirOrdemServicoUseCase _abrirUseCase;
    private readonly ListarOrdensServicoUseCase _listarUseCase;
    private readonly ObterOrdemServicoUseCase _obterUseCase;
    private readonly ConsultarStatusOSUseCase _consultarStatusUseCase;
    private readonly AtualizarStatusOSUseCase _atualizarStatusUseCase;

    public OrdensServicoController(
        AbrirOrdemServicoUseCase abrirUseCase,
        ListarOrdensServicoUseCase listarUseCase,
        ObterOrdemServicoUseCase obterUseCase,
        ConsultarStatusOSUseCase consultarStatusUseCase,
        AtualizarStatusOSUseCase atualizarStatusUseCase)
    {
        _abrirUseCase = abrirUseCase;
        _listarUseCase = listarUseCase;
        _obterUseCase = obterUseCase;
        _consultarStatusUseCase = consultarStatusUseCase;
        _atualizarStatusUseCase = atualizarStatusUseCase;
    }

    /// <summary>Lista as OS em andamento, da mais urgente (EmExecucao) para a menos (EmDiagnostico).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyCollection<ListarOrdensServicoItem>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Listar(CancellationToken ct)
        => Ok(await _listarUseCase.ExecutarAsync(ct));

    /// <summary>Abre uma OS para cliente, veículo e filial. A OS nasce em EmDiagnostico.</summary>
    /// <response code="201">OS criada; corpo traz o ID e o número consultáveis.</response>
    /// <response code="400">Campo obrigatório ausente; nenhuma OS é criada.</response>
    /// <response code="404">Veículo ou filial inexistente.</response>
    /// <response code="422">Veículo não pertence ao cliente ou filial inativa.</response>
    [HttpPost]
    [Authorize(Roles = "Admin,Atendente")]
    [ProducesResponseType(typeof(AbrirOrdemServicoResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Abrir([FromBody] AbrirOrdemServicoRequest request, CancellationToken ct)
    {
        var resultado = await _abrirUseCase.ExecutarAsync(request, ct);
        return CreatedAtAction(nameof(ObterPorId), new { id = resultado.Id }, resultado);
    }

    /// <summary>Obtém a OS com as referências de cliente, veículo e filial e o histórico.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(OrdemServicoDetalheResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ObterPorId(Guid id, CancellationToken ct)
    {
        var os = await _obterUseCase.ExecutarAsync(id, ct);
        return os is null ? NotFound() : Ok(os);
    }

    /// <summary>Consulta o status atual e as transições já registradas.</summary>
    [HttpGet("{id:guid}/status")]
    [ProducesResponseType(typeof(ConsultarStatusOSResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ConsultarStatus(Guid id, CancellationToken ct)
        => Ok(await _consultarStatusUseCase.ExecutarAsync(id, ct));

    /// <summary>Histórico de transições de status da OS, em ordem de registro.</summary>
    [HttpGet("{id:guid}/historico")]
    [ProducesResponseType(typeof(IReadOnlyCollection<HistoricoStatusOSItem>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ConsultarHistorico(Guid id, CancellationToken ct)
    {
        var status = await _consultarStatusUseCase.ExecutarAsync(id, ct);
        return Ok(status.Historico.OrderBy(h => h.AlteradoEm).ToList());
    }

    /// <summary>Registra a entrega do veículo ao cliente (Finalizada → Entregue). Fora da Saga.</summary>
    /// <response code="422">OS não está Finalizada.</response>
    [HttpPost("{id:guid}/entrega")]
    [Authorize(Roles = "Admin,Atendente")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> RegistrarEntrega(Guid id, CancellationToken ct)
    {
        await _atualizarStatusUseCase.ExecutarAsync(id, StatusOrdemServico.Entregue, ct);
        return NoContent();
    }

    /// <summary>Cancela a OS a partir de um status não terminal.</summary>
    /// <response code="422">OS Finalizada, Entregue ou já Cancelada.</response>
    [HttpPost("{id:guid}/cancelamento")]
    [Authorize(Roles = "Admin,Atendente")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Cancelar(Guid id, CancellationToken ct)
    {
        await _atualizarStatusUseCase.ExecutarAsync(id, StatusOrdemServico.Cancelada, ct);
        return NoContent();
    }
}
