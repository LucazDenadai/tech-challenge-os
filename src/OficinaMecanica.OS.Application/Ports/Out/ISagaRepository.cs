using OficinaMecanica.OS.Application.Contratos.Saga;
using OficinaMecanica.OS.Domain.Saga;

namespace OficinaMecanica.OS.Application.Ports.Out;

public interface ISagaRepository
{
    Task AdicionarAsync(SagaOrdemServico saga, CancellationToken ct = default);

    // Saga com itens do diagnóstico e transições, para aplicar a próxima mensagem.
    Task<SagaOrdemServico?> ObterPorOsAsync(Guid osId, CancellationToken ct = default);

    Task<SagaOrdemServico?> ObterAsync(Guid sagaId, CancellationToken ct = default);

    // Sagas em estado com prazo cuja etapa começou até o limite. O filtro fino (reenvios já feitos) é do domínio.
    Task<IReadOnlyList<Guid>> ListarComPrazoVencidoAsync(DateTime etapaIniciadaAte, int maximo, CancellationToken ct = default);
}

public interface IOutbox
{
    // Inclui o comando na transação corrente; o despachante publica depois do commit (ADR-016).
    void Adicionar(MensagemSaga mensagem);
}

public interface ITransacao
{
    // Inbox, transição da Saga, status da OS e outbox em uma transação: ou tudo é confirmado, ou nada.
    Task<T> ExecutarAsync<T>(Func<Task<T>> acao, CancellationToken ct = default);
}
