using System.Diagnostics.CodeAnalysis;
using OficinaMecanica.OS.Domain.Enums;

namespace OficinaMecanica.OS.Domain.Entities;

public class OrdemServico : EntityBase
{
    private static readonly StatusOrdemServico[] _cancelaveis =
    [
        StatusOrdemServico.EmDiagnostico,
        StatusOrdemServico.AguardandoAprovacao,
        StatusOrdemServico.AguardandoPagamento,
        StatusOrdemServico.EmExecucao
    ];

    public string Numero { get; private set; } = string.Empty;
    public Guid ClienteId { get; private set; }
    public Guid VeiculoId { get; private set; }
    public Guid FilialId { get; private set; }
    public StatusOrdemServico Status { get; private set; } = StatusOrdemServico.EmDiagnostico;
    public string Observacoes { get; private set; } = string.Empty;
    public DateTime DataAbertura { get; private set; } = DateTime.UtcNow;
    public DateTime? DataFechamento { get; private set; }

    public Cliente? Cliente { get; protected set; }
    public Veiculo? Veiculo { get; protected set; }
    public Filial? Filial { get; protected set; }

    private readonly List<HistoricoStatusOS> _historico = new();
    public IReadOnlyCollection<HistoricoStatusOS> Historico => _historico.AsReadOnly();

    [ExcludeFromCodeCoverage]
    protected OrdemServico() { }

    // A OS nasce solicitando diagnóstico (ADR-017); o status "Recebida" da Fase 3 não existe mais.
    public OrdemServico(string numero, Guid clienteId, Guid veiculoId, Guid filialId, string observacoes)
    {
        if (filialId == Guid.Empty)
            throw new ArgumentException("A filial da OS é obrigatória.", nameof(filialId));

        Numero = numero;
        ClienteId = clienteId;
        VeiculoId = veiculoId;
        FilialId = filialId;
        Observacoes = observacoes;
    }

    public void AlterarStatus(StatusOrdemServico novoStatus)
    {
        if (Status is StatusOrdemServico.Entregue or StatusOrdemServico.Cancelada)
            throw new InvalidOperationException($"OS no status '{Status}' não pode ser alterada.");

        var statusAnterior = Status;

        if (novoStatus == StatusOrdemServico.Cancelada)
        {
            if (!_cancelaveis.Contains(Status))
                throw new InvalidOperationException($"OS no status '{Status}' não pode ser cancelada.");

            Status = StatusOrdemServico.Cancelada;
            DataFechamento = DateTime.UtcNow;
            _historico.Add(new HistoricoStatusOS(Id, statusAnterior, Status));
            MarcarAtualizado();
            return;
        }

        var proximoEsperado = Status switch
        {
            StatusOrdemServico.EmDiagnostico => StatusOrdemServico.AguardandoAprovacao,
            StatusOrdemServico.AguardandoAprovacao => StatusOrdemServico.AguardandoPagamento,
            StatusOrdemServico.AguardandoPagamento => StatusOrdemServico.EmExecucao,
            StatusOrdemServico.EmExecucao => StatusOrdemServico.Finalizada,
            StatusOrdemServico.Finalizada => StatusOrdemServico.Entregue,
            _ => throw new InvalidOperationException("Status inválido.")
        };

        if (novoStatus != proximoEsperado)
            throw new InvalidOperationException($"O próximo status esperado é '{proximoEsperado}'.");

        Status = novoStatus;

        if (Status is StatusOrdemServico.Finalizada or StatusOrdemServico.Entregue)
            DataFechamento = DateTime.UtcNow;

        _historico.Add(new HistoricoStatusOS(Id, statusAnterior, Status));
        MarcarAtualizado();
    }

    public void AtualizarObservacoes(string observacoes)
    {
        Observacoes = observacoes;
        MarcarAtualizado();
    }
}
