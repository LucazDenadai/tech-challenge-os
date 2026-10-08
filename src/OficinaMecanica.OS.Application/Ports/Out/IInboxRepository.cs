using OficinaMecanica.OS.Application.Contratos.Saga;

namespace OficinaMecanica.OS.Application.Ports.Out;

public interface IInboxRepository
{
    // Grava a mensagem na inbox do OS. Retorna false quando o messageId já foi registrado (duplicata).
    Task<bool> RegistrarAsync(MensagemSaga mensagem, string canal, string payload, CancellationToken ct = default);
}
