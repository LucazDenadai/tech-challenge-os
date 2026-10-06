using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OficinaMecanica.OS.Application.Ports.Out;
using OficinaMecanica.OS.Infrastructure.Adapters.Out.Email;
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

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddScoped<IOrdemServicoRepository, OrdemServicoRepository>();
        services.AddScoped<IClienteRepository, ClienteRepository>();
        services.AddScoped<IVeiculoRepository, VeiculoRepository>();
        services.AddScoped<IUsuarioRepository, UsuarioRepository>();
        services.AddScoped<IFilialRepository, FilialRepository>();

        services.AddScoped<ITokenService, JwtTokenService>();

        var usarEmailReal = configuration.GetValue<bool>("Smtp:Enabled");
        if (usarEmailReal)
            services.AddScoped<IEmailPort, EmailSmtpAdapter>();
        else
            services.AddScoped<IEmailPort, EmailStub>();

        return services;
    }
}
