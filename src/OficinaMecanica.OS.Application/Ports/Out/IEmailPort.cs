using OficinaMecanica.OS.Domain.Enums;

namespace OficinaMecanica.OS.Application.Ports.Out;

public interface IEmailPort
{
    Task EnviarAtualizacaoStatusAsync(string destinatario, string numeroOS, StatusOrdemServico novoStatus, CancellationToken ct = default);
}
