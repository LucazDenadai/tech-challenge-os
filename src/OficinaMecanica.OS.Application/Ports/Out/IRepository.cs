namespace OficinaMecanica.OS.Application.Ports.Out;

public interface IRepository<T> where T : class
{
    Task<T?> ObterPorIdAsync(Guid id, CancellationToken ct = default);
    Task<IEnumerable<T>> ObterTodosAsync(CancellationToken ct = default);
    Task AdicionarAsync(T entidade, CancellationToken ct = default);
    Task AtualizarAsync(T entidade, CancellationToken ct = default);
    Task RemoverAsync(T entidade, CancellationToken ct = default);
    Task<int> SalvarAsync(CancellationToken ct = default);
}
