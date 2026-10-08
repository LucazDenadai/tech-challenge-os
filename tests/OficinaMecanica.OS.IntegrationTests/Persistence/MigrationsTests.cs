using Microsoft.EntityFrameworkCore;
using OficinaMecanica.OS.IntegrationTests.Fixtures;

namespace OficinaMecanica.OS.IntegrationTests.Persistence;

[Collection(PostgresCollection.Nome)]
public class MigrationsTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Migrations_BancoVazio_CriaSomenteEstruturasDoOS()
    {
        // ADR-015/016: o banco do OS não pode conter tabelas de Billing/Operações nem schemas legados da Fase 3.
        await using var db = PostgresFixture.CriarContexto(fixture.NovoBancoVazio());

        await db.Database.MigrateAsync();

        var tabelas = await db.Database
            .SqlQueryRaw<string>("""
                SELECT table_schema || '.' || table_name AS "Value"
                FROM information_schema.tables
                WHERE table_schema NOT IN ('pg_catalog', 'information_schema')
                """)
            .ToListAsync();

        Assert.Equal(
            new[]
            {
                "public.Clientes", "public.Filiais", "public.HistoricoStatusOS", "public.InboxMensagens", "public.OrdensServico",
                "public.Usuarios", "public.Veiculos", "public.__EFMigrationsHistory"
            }.Order(),
            tabelas.Order());
    }

    [Fact]
    public async Task Migrations_BancoVazio_NaoTemMudancasPendentesNoModelo()
    {
        // Garante que a migration versionada reflete o modelo atual (ninguém alterou entidade sem gerar migration).
        await using var db = PostgresFixture.CriarContexto(fixture.NovoBancoVazio());

        await db.Database.MigrateAsync();

        Assert.False(db.Database.HasPendingModelChanges());
    }
}
