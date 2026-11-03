using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using OficinaMecanica.OS.Application.Ports.Out;
using OficinaMecanica.OS.Domain.Enums;
using OficinaMecanica.OS.Infrastructure;
using OficinaMecanica.OS.Infrastructure.Adapters.In.Agendamento;
using OficinaMecanica.OS.Infrastructure.Adapters.In.Messaging;
using OficinaMecanica.OS.Infrastructure.Adapters.Out.Email;
using OficinaMecanica.OS.Infrastructure.Adapters.Out.Security;
using OficinaMecanica.OS.Infrastructure.Adapters.Out.Stubs;

namespace OficinaMecanica.OS.IntegrationTests.Infraestrutura;

// Configuração na subida, e-mail, JWT e consumidor: bordas que não dependem de container.
// Os testes de JWT mexem em variáveis de ambiente do processo; ficam numa coleção própria, sem paralelismo.
[Collection(nameof(ConfiguracaoEBordasTests))]
public class ConfiguracaoEBordasTests
{
    private const string Conexao = "Host=localhost;Database=x";

    private static IConfiguration Config(params (string Chave, string Valor)[] valores)
        => new ConfigurationBuilder().AddInMemoryCollection(valores.Select(v => new KeyValuePair<string, string?>(v.Chave, v.Valor))).Build();

    [Fact]
    public void Infraestrutura_SemConnectionString_NaoSobe()
        => Assert.Contains("DefaultConnection", Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddInfrastructure(Config())).Message);

    [Fact]
    public void Infraestrutura_PadroesSemSecoes_PrazosLigadosEEmailSimulado()
    {
        var servicos = new ServiceCollection().AddInfrastructure(Config(("ConnectionStrings:DefaultConnection", Conexao)));

        Assert.Contains(servicos, s => s.ImplementationType == typeof(PrazosSagaHostedService));
        Assert.Contains(servicos, s => s.ServiceType == typeof(IEmailPort) && s.ImplementationType == typeof(EmailStub));
    }

    [Fact]
    public void Infraestrutura_PrazosDesligadosEEmailReal()
    {
        var servicos = new ServiceCollection().AddInfrastructure(Config(("ConnectionStrings:DefaultConnection", Conexao),
            ("Saga:VerificacaoHabilitada", "false"), ("Smtp:Enabled", "true")));

        Assert.DoesNotContain(servicos, s => s.ImplementationType == typeof(PrazosSagaHostedService));
        Assert.Contains(servicos, s => s.ServiceType == typeof(IEmailPort) && s.ImplementationType == typeof(EmailSmtpAdapter));
    }

    [Fact]
    public void Mensageria_SemSecao_NaoRegistraConsumidor()
        => Assert.DoesNotContain(new ServiceCollection().AddMensageria(Config()), s => s.ImplementationType == typeof(ConsumidorSagaHostedService));

    [Theory]
    [InlineData("", "senha")]
    [InlineData("os", "")]
    public void Mensageria_HabilitadaSemCredencial_NaoSobe(string usuario, string senha)
    {
        var config = Config(("RabbitMq:Enabled", "true"), ("RabbitMq:Username", usuario), ("RabbitMq:Password", senha));

        Assert.Contains("RabbitMq:Username", Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddMensageria(config)).Message);
    }

    [Fact]
    public async Task HealthCheckDoConsumidor_SegueOEstado()
    {
        var estado = new EstadoConsumidorSaga();
        var check = new ConsumidorSagaHealthCheck(estado);

        Assert.Equal(HealthStatus.Unhealthy, (await check.CheckHealthAsync(new HealthCheckContext())).Status);
        estado.Marcar(true);
        Assert.Equal(HealthStatus.Healthy, (await check.CheckHealthAsync(new HealthCheckContext())).Status);
    }

    [Fact]
    public async Task EmailSimulado_SoRegistraEmLog()
        => await new EmailStub(NullLogger<EmailStub>.Instance).EnviarAtualizacaoStatusAsync("cliente@cliente.example", "OS-2026-0001", StatusOrdemServico.Finalizada);

    // O envio real exige STARTTLS com certificado confiável; sem servidor SMTP, a falha chega a quem chamou.
    [Fact]
    public async Task EmailReal_ServidorForaDoAr_FalhaParaQuemChamou()
    {
        var email = new EmailSmtpAdapter(Config(("Smtp:Host", "127.0.0.1"), ("Smtp:Port", "1"), ("Smtp:User", "oficina@oficina.example")));

        await Assert.ThrowsAnyAsync<Exception>(() =>
            email.EnviarAtualizacaoStatusAsync("cliente@cliente.example", "OS-2026-0001", StatusOrdemServico.Finalizada));
    }

    [Fact]
    public void Jwt_ConfiguradoPeloAppSettings()
    {
        var servico = new JwtTokenService(Config(("Jwt:Key", new string('k', 40)), ("Jwt:Issuer", "emissor"), ("Jwt:Audience", "publico"),
            ("Jwt:ExpiracaoMinutos", "5")));

        var token = new JwtSecurityTokenHandler().ReadJwtToken(servico.GerarToken(Guid.NewGuid(), "admin@oficina.example", "Admin"));

        Assert.Equal("emissor", token.Issuer);
        Assert.Equal("publico", token.Audiences.Single());
        Assert.InRange(token.ValidTo - DateTime.UtcNow, TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(5.1));
    }

    [Fact]
    public void Jwt_SemConfiguracao_UsaVariaveisDeAmbiente()
    {
        var variaveis = new Dictionary<string, string>
        {
            ["JWT_KEY"] = new string('e', 40), ["JWT_ISSUER"] = "emissor-env", ["JWT_AUDIENCE"] = "publico-env", ["JWT_EXPIRACAO_MINUTOS"] = "10"
        };
        try
        {
            foreach (var (nome, valor) in variaveis)
                Environment.SetEnvironmentVariable(nome, valor);

            var token = new JwtSecurityTokenHandler().ReadJwtToken(
                new JwtTokenService(Config(("Jwt:Key", ""))).GerarToken(Guid.NewGuid(), "admin@oficina.example", "Admin"));

            Assert.Equal("emissor-env", token.Issuer);
            Assert.Equal("publico-env", token.Audiences.Single());
        }
        finally
        {
            foreach (var nome in variaveis.Keys)
                Environment.SetEnvironmentVariable(nome, null);
        }
    }

    [Fact]
    public void Jwt_SemChaveEmLugarNenhum_NaoGeraToken()
        => Assert.Throws<InvalidOperationException>(() => new JwtTokenService(Config()).GerarToken(Guid.NewGuid(), "admin@oficina.example", "Admin"));
}

[CollectionDefinition(nameof(ConfiguracaoEBordasTests), DisableParallelization = true)]
public class ConfiguracaoEBordasColecao;
