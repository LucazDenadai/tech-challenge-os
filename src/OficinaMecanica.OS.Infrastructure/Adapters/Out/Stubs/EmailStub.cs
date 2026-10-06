using Microsoft.Extensions.Logging;
using OficinaMecanica.OS.Application.Ports.Out;
using OficinaMecanica.OS.Domain.Enums;

namespace OficinaMecanica.OS.Infrastructure.Adapters.Out.Stubs;

// Substituído por adaptador real de email no CARD-10
public class EmailStub : IEmailPort
{
    private readonly ILogger<EmailStub> _logger;

    public EmailStub(ILogger<EmailStub> logger) => _logger = logger;

    public Task EnviarAtualizacaoStatusAsync(string destinatario, string numeroOS, StatusOrdemServico novoStatus, CancellationToken ct = default)
    {
        _logger.LogInformation("Email enviado para: {Destinatario} | OS: {Numero} | Status: {Status}", destinatario, numeroOS, novoStatus);
        return Task.CompletedTask;
    }
}
