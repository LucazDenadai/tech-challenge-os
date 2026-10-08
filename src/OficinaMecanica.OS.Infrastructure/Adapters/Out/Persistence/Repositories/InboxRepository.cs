using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using OficinaMecanica.OS.Application.Contratos.Saga;
using OficinaMecanica.OS.Application.Ports.Out;

namespace OficinaMecanica.OS.Infrastructure.Adapters.Out.Persistence.Repositories;

public class InboxRepository(AppDbContext context) : IInboxRepository
{
    // ON CONFLICT torna a deduplicação atômica: duas entregas simultâneas do mesmo messageId gravam uma linha só.
    public async Task<bool> RegistrarAsync(MensagemSaga mensagem, string canal, string payload, CancellationToken ct = default)
    {
        var linhas = await context.Database.ExecuteSqlRawAsync("""
            INSERT INTO "InboxMensagens"
                ("MessageId", "MessageType", "Canal", "Producer", "CorrelationId", "CausationId",
                 "OsId", "FilialId", "OccurredAtUtc", "RecebidaEmUtc", "Payload")
            VALUES (@messageId, @messageType, @canal, @producer, @correlationId, @causationId,
                    @osId, @filialId, @occurredAtUtc, @recebidaEmUtc, @payload)
            ON CONFLICT ("MessageId") DO NOTHING
            """,
            [
                new NpgsqlParameter("messageId", mensagem.MessageId),
                new NpgsqlParameter("messageType", mensagem.MessageType),
                new NpgsqlParameter("canal", canal),
                new NpgsqlParameter("producer", mensagem.Producer),
                new NpgsqlParameter("correlationId", mensagem.CorrelationId),
                new NpgsqlParameter("causationId", NpgsqlDbType.Uuid) { Value = (object?)mensagem.CausationId ?? DBNull.Value },
                new NpgsqlParameter("osId", mensagem.OsId),
                new NpgsqlParameter("filialId", mensagem.FilialId),
                new NpgsqlParameter("occurredAtUtc", mensagem.OccurredAtUtc.ToUniversalTime()),
                new NpgsqlParameter("recebidaEmUtc", DateTimeOffset.UtcNow),
                new NpgsqlParameter("payload", NpgsqlDbType.Jsonb) { Value = payload }
            ],
            ct);

        return linhas == 1;
    }
}
