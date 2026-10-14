using Microsoft.EntityFrameworkCore;
using Npgsql;
using OficinaMecanica.OS.Infrastructure.Adapters.Out.Persistence;
using Testcontainers.PostgreSql;

namespace OficinaMecanica.OS.IntegrationTests.Fixtures;

public class PostgresFixture : IAsyncLifetime
{
    // Um servidor para todos os testes, com um banco por teste: cada banco tem o próprio pool de conexões, e cada API
    // de teste roda os serviços em segundo plano (prazos, estado das Sagas). O limite padrão de 100 conexões não basta.
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("os_test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .WithCommand("-c", "max_connections=300")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.StopAsync();

    // Cada teste recebe um banco novo e vazio, para que migrations e asserções não dependam de ordem de execução.
    public string NovoBancoVazio()
    {
        var builder = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Database = $"os_{Guid.NewGuid():N}"
        };
        return builder.ConnectionString;
    }

    public static AppDbContext CriarContexto(string connectionString)
        => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options);
}

[CollectionDefinition(Nome)]
public class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Nome = "Postgres";
}
