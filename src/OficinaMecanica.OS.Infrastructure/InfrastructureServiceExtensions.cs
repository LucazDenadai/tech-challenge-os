using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OficinaMecanica.OS.Application.Ports.Out;
using OficinaMecanica.OS.Application.UseCases.Saga;
using OficinaMecanica.OS.Infrastructure.Adapters.In.Agendamento;
using OficinaMecanica.OS.Infrastructure.Adapters.In.Messaging;
using OficinaMecanica.OS.Infrastructure.Adapters.Out.Email;
using OficinaMecanica.OS.Infrastructure.Adapters.Out.Messaging;
using OficinaMecanica.OS.Infrastructure.Adapters.Out.Persistence;
using OficinaMecanica.OS.Infrastructure.Adapters.Out.Persistence.Repositories;
using OficinaMecanica.OS.Infrastructure.Adapters.Out.Security;
using OficinaMecanica.OS.Infrastructure.Adapters.Out.Stubs;

namespace OficinaMecanica.OS.Infrastructure;

public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Instância PostgreSQL dedicada ao OS (ADR-016); sem fallback para outro banco.
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' não configurada.");

        // O interceptor mede a duração das Sagas que terminam (CARD-42a); guarda o estado por DbContext, então uma instância serve a todos.
        var metricasSaga = new MetricasSagaInterceptor();
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString).AddInterceptors(metricasSaga));

        services.AddScoped<IOrdemServicoRepository, OrdemServicoRepository>();
        services.AddScoped<IClienteRepository, ClienteRepository>();
        services.AddScoped<IVeiculoRepository, VeiculoRepository>();
        services.AddScoped<IUsuarioRepository, UsuarioRepository>();
        services.AddScoped<IFilialRepository, FilialRepository>();
        services.AddScoped<IInboxRepository, InboxRepository>();
        services.AddScoped<ISagaRepository, SagaRepository>();
        services.AddScoped<IOutbox, OutboxRepository>();
        services.AddScoped<ITransacao, Transacao>();

        services.AddScoped<ITokenService, JwtTokenService>();

        // Prazos da Saga (CARD-40b): não depende do broker, os comandos vão para a outbox.
        var configuracaoSaga = configuration.GetSection(ConfiguracaoSaga.Secao).Get<ConfiguracaoSaga>() ?? new ConfiguracaoSaga();
        services.AddSingleton(configuracaoSaga);
        if (configuracaoSaga.VerificacaoHabilitada)
            services.AddHostedService<PrazosSagaHostedService>();
        services.AddHostedService<EstadoSagasHostedService>();

        var usarEmailReal = configuration.GetValue<bool>("Smtp:Enabled");
        if (usarEmailReal)
            services.AddScoped<IEmailPort, EmailSmtpAdapter>();
        else
            services.AddScoped<IEmailPort, EmailStub>();

        return services;
    }

    // Consumidor dos canais da Saga e despachante da outbox (ADR-018). Desabilitado, nenhuma conexão é aberta.
    public static IServiceCollection AddMensageria(this IServiceCollection services, IConfiguration configuration)
    {
        var secao = configuration.GetSection(RabbitMqOptions.Secao);
        var opcoes = secao.Get<RabbitMqOptions>() ?? new RabbitMqOptions();
        if (!opcoes.Enabled)
            return services;

        // Conexão autenticada: sem usuário e senha configurados o serviço não sobe (não usa guest/guest implícito).
        if (string.IsNullOrWhiteSpace(opcoes.Username) || string.IsNullOrWhiteSpace(opcoes.Password))
            throw new InvalidOperationException("RabbitMq:Username e RabbitMq:Password são obrigatórios quando RabbitMq:Enabled=true.");

        services.Configure<RabbitMqOptions>(secao);
        services.AddSingleton<EstadoConsumidorSaga>();
        services.AddHostedService<ConsumidorSagaHostedService>();
        services.AddSingleton<PublicadorRabbitMq>();
        services.AddHostedService<DespachanteOutboxHostedService>();
        return services;
    }
}
