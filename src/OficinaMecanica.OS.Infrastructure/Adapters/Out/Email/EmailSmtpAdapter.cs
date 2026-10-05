using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using MimeKit;
using OficinaMecanica.OS.Application.Ports.Out;
using OficinaMecanica.OS.Domain.Enums;

namespace OficinaMecanica.OS.Infrastructure.Adapters.Out.Email;

public class EmailSmtpAdapter(IConfiguration configuration) : IEmailPort
{
    public async Task EnviarAtualizacaoStatusAsync(string destinatario, string numeroOS, StatusOrdemServico novoStatus, CancellationToken ct = default)
    {
        var host = configuration["Smtp:Host"] ?? "localhost";
        var port = configuration.GetValue<int>("Smtp:Port", 587);
        var user = configuration["Smtp:User"] ?? string.Empty;
        var password = configuration["Smtp:Pwd"] ?? string.Empty;
        var from = configuration["Smtp:From"] ?? user;

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(from));
        message.To.Add(MailboxAddress.Parse(destinatario));
        message.Subject = $"[Oficina] OS {numeroOS} — Status atualizado";
        message.Body = new TextPart("plain")
        {
            Text = $"""
                Sua ordem de serviço {numeroOS} teve o status atualizado para:
                {novoStatus}

                Acesse nosso sistema para mais detalhes.
                """
        };

        using var smtp = new SmtpClient();
        await smtp.ConnectAsync(host, port, SecureSocketOptions.StartTls, ct);

        if (!string.IsNullOrEmpty(user))
            await smtp.AuthenticateAsync(user, password, ct);

        await smtp.SendAsync(message, ct);
        await smtp.DisconnectAsync(true, ct);
    }
}
