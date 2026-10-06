using Microsoft.EntityFrameworkCore;
using OficinaMecanica.OS.Application.Ports.Out;
using OficinaMecanica.OS.Domain.Enums;
using OficinaMecanica.OS.Infrastructure.Adapters.Out.Persistence.Seed;
using OficinaMecanica.OS.IntegrationTests.Fixtures;

namespace OficinaMecanica.OS.IntegrationTests.Persistence;

[Collection(PostgresCollection.Nome)]
public class SeedDemonstracaoTests(PostgresFixture fixture)
{
    private const string Senha = "Senha@Teste1";

    [Fact]
    public async Task Seed_ExecutadoDuasVezes_NaoDuplicaRegistros()
    {
        // O seed roda a cada inicialização do serviço; precisa ser idempotente.
        var conn = fixture.NovoBancoVazio();
        await using (var db = PostgresFixture.CriarContexto(conn))
        {
            await db.Database.MigrateAsync();
            await SeedDemonstracao.ExecutarAsync(db, new HashFake(), Senha);
        }
        await using (var db = PostgresFixture.CriarContexto(conn))
            await SeedDemonstracao.ExecutarAsync(db, new HashFake(), Senha);

        await using var leitura = PostgresFixture.CriarContexto(conn);
        Assert.Equal(1, await leitura.Filiais.CountAsync());
        Assert.Equal(2, await leitura.Clientes.CountAsync());
        Assert.Equal(2, await leitura.Veiculos.CountAsync());
        Assert.Equal(
            [PerfilUsuario.Admin, PerfilUsuario.Atendente, PerfilUsuario.Mecanico],
            (await leitura.Usuarios.Select(u => u.Perfil).ToListAsync()).Order());
    }

    [Fact]
    public async Task Seed_NaoUsaDadosPessoaisReais()
    {
        var conn = fixture.NovoBancoVazio();
        await using var db = PostgresFixture.CriarContexto(conn);
        await db.Database.MigrateAsync();

        await SeedDemonstracao.ExecutarAsync(db, new HashFake(), Senha);

        // Domínios .example são reservados (RFC 2606): nenhum e-mail do seed alcança uma pessoa real.
        var emails = await db.Clientes.Select(c => c.Email).Concat(db.Usuarios.Select(u => u.Email)).ToListAsync();
        Assert.All(emails, e => Assert.EndsWith(".example", e));
        Assert.All(await db.Usuarios.Select(u => u.SenhaHash).ToListAsync(), h => Assert.NotEqual(Senha, h));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Seed_SemSenhaConfigurada_FalhaSemGravarNada(string senha)
    {
        var conn = fixture.NovoBancoVazio();
        await using var db = PostgresFixture.CriarContexto(conn);
        await db.Database.MigrateAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => SeedDemonstracao.ExecutarAsync(db, new HashFake(), senha));
        Assert.False(await db.Usuarios.AnyAsync());
    }

    private sealed class HashFake : ITokenService
    {
        public string GerarToken(Guid usuarioId, string email, string perfil) => throw new NotSupportedException();
        public bool VerificarSenha(string senhaPlana, string hashArmazenado) => throw new NotSupportedException();
        public string HashSenha(string senhaPlana) => $"hash:{senhaPlana.Length}";
    }
}
