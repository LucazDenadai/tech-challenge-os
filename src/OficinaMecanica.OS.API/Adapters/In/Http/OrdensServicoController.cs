using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OficinaMecanica.OS.Application.UseCases.OrdemServico;
using System.Security.Claims;
using OficinaMecanica.OS.Application.UseCases.Saga;
using OficinaMecanica.OS.Domain.Enums;
using OficinaMecanica.OS.Domain.Saga;

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
    private readonly ConsultarSagaUseCase _consultarSagaUseCase;
    private readonly CancelarOrdemServicoUseCase _cancelarUseCase;
    private readonly ResolverAcaoManualUseCase _resolverUseCase;

    public OrdensServicoController(
        AbrirOrdemServicoUseCase abrirUseCase,
        ListarOrdensServicoUseCase listarUseCase,
        ObterOrdemServicoUseCase obterUseCase,
        ConsultarStatusOSUseCase consultarStatusUseCase,
        AtualizarStatusOSUseCase atualizarStatusUseCase,
        ConsultarSagaUseCase consultarSagaUseCase,
        CancelarOrdemServicoUseCase cancelarUseCase,
        ResolverAcaoManualUseCase resolverUseCase)
    {
        _abrirUseCase = abrirUseCase;
        _listarUseCase = listarUseCase;
        _obterUseCase = obterUseCase;
        _consultarStatusUseCase = consultarStatusUseCase;
        _atualizarStatusUseCase = atualizarStatusUseCase;
        _consultarSagaUseCase = consultarSagaUseCase;
        _cancelarUseCase = cancelarUseCase;
        _resolverUseCase = resolverUseCase;
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

    /// <summary>Estado da Saga da OS, referências de orçamento, reserva, pagamento e execução, e as transições.</summary>
    /// <remarks>Lido só do banco do OS. Não traz dados do cliente.</remarks>
    [HttpGet("{id:guid}/saga")]
    [Authorize(Roles = "Admin,Atendente")]
    [ProducesResponseType(typeof(SagaOrdemServicoResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ConsultarSaga(Guid id, CancellationToken ct)
        => Ok(await _consultarSagaUseCase.ExecutarAsync(id, ct));

    /// <summary>Resolve uma Saga parada em ação manual: cancelar com compensação ou retomar a etapa (ADR-020).</summary>
    /// <remarks>O motivo e o usuário ficam no histórico da Saga. Pagamento divergente só aceita Cancelar.</remarks>
    /// <response code="204">Resolução registrada; compensação ou retomada seguem pela Saga.</response>
    /// <response code="400">Motivo ausente.</response>
    /// <response code="422">A Saga não está em ação manual, ou Retomar não vale para a causa.</response>
    [HttpPost("{id:guid}/saga/resolucao")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ResolverAcaoManual(Guid id, [FromBody] ResolverAcaoManualRequest request, CancellationToken ct)
    {
        var usuario = User.FindFirst(ClaimTypes.Email)?.Value ?? User.FindFirst("email")?.Value ?? "admin";
        await _resolverUseCase.ExecutarAsync(id, request.Acao, request.Motivo, usuario, ct);
        return NoContent();
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

    /// <summary>Pede o cancelamento da OS pela Saga (ADR-020).</summary>
    /// <remarks>Sem efeito a desfazer, encerra na hora (204). Com reserva, pagamento ou execução em andamento, a Saga
    /// libera, estorna ou pede o cancelamento a Operações (202): o resultado aparece no status e na Saga. Com o reparo
    /// já começado, Operações recusa e a OS segue em execução.</remarks>
    /// <response code="202">Cancelamento pedido; a Saga compensa ou espera a resposta de um participante.</response>
    /// <response code="422">OS encerrada ou Saga em ação manual.</response>
    [HttpPost("{id:guid}/cancelamento")]
    [Authorize(Roles = "Admin,Atendente")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Cancelar(Guid id, CancellationToken ct)
    {
        var resultado = await _cancelarUseCase.ExecutarAsync(id, ct);
        return resultado == ResultadoCancelamento.Encerrada ? NoContent() : Accepted();
    }
}
