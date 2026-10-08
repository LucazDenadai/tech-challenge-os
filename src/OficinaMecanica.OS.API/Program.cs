using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using OficinaMecanica.OS.API.Auth;
using OficinaMecanica.OS.API.Extensions;
using OficinaMecanica.OS.API.Filters;
using OficinaMecanica.OS.API.Middleware;
using OficinaMecanica.OS.Application.Ports.Out;
using OficinaMecanica.OS.Application.UseCases.Auth;
using OficinaMecanica.OS.Application.UseCases.Cliente;
using OficinaMecanica.OS.Application.UseCases.Filial;
using OficinaMecanica.OS.Application.UseCases.Mensageria;
using OficinaMecanica.OS.Application.UseCases.OrdemServico;
using OficinaMecanica.OS.Application.UseCases.Usuario;
using OficinaMecanica.OS.Application.UseCases.Veiculo;
using OficinaMecanica.OS.Infrastructure;
using OficinaMecanica.OS.Infrastructure.Adapters.In.Messaging;
using OficinaMecanica.OS.Infrastructure.Adapters.Out.Persistence;
using OficinaMecanica.OS.Infrastructure.Adapters.Out.Persistence.Seed;

var builder = WebApplication.CreateBuilder(args);

// ── Logging estruturado em JSON (escopos carregam o CorrelationId) ─────────────
builder.Logging.AddJsonConsole(o =>
{
    o.IncludeScopes = true;
    o.TimestampFormat = "O";
    o.JsonWriterOptions = new System.Text.Json.JsonWriterOptions { Indented = false };
});

// ── OpenTelemetry (traces OTLP, métricas Prometheus) ───────────────────────────
builder.AddOpenTelemetry("OficinaMecanica.OS");

// ── Infrastructure (banco OS, repositórios, JWT, e-mail) e consumidor da Saga ──
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddMensageria(builder.Configuration);

// ── Use Cases ──────────────────────────────────────────────────────────────────
builder.Services.AddScoped<AuthUseCase>();
builder.Services.AddScoped<GerenciarClienteUseCase>();
builder.Services.AddScoped<BuscarClientePorCpfUseCase>();
builder.Services.AddScoped<GerenciarVeiculoUseCase>();
builder.Services.AddScoped<GerenciarUsuarioUseCase>();
builder.Services.AddScoped<ListarFiliaisUseCase>();
builder.Services.AddScoped<AbrirOrdemServicoUseCase>();
builder.Services.AddScoped<ListarOrdensServicoUseCase>();
builder.Services.AddScoped<ObterOrdemServicoUseCase>();
builder.Services.AddScoped<ConsultarStatusOSUseCase>();
builder.Services.AddScoped<AtualizarStatusOSUseCase>();
builder.Services.AddScoped<AcompanharOSUseCase>();
builder.Services.AddScoped<RegistrarMensagemRecebidaUseCase>();

// ── Autenticação: JWT (funcionários e clientes) e chave de API interna (Lambda) ─
var jwtKey = (builder.Configuration["Jwt:Key"] is { Length: > 0 } k ? k : null)
    ?? Environment.GetEnvironmentVariable("JWT_KEY")
    ?? throw new InvalidOperationException("JWT_KEY não configurado.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? Environment.GetEnvironmentVariable("JWT_ISSUER"),
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? Environment.GetEnvironmentVariable("JWT_AUDIENCE"),
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    })
    .AddScheme<AuthenticationSchemeOptions, ApiKeyInternaHandler>(ApiKeyInternaHandler.Esquema, null);

builder.Services.AddAuthorization();

// ── Rate Limiting (10 req/min por IP no login) ─────────────────────────────────
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("login", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// ── CORS ───────────────────────────────────────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>();
        if (origins is { Length: > 0 })
            policy.WithOrigins(origins).AllowAnyMethod().AllowAnyHeader();
        else if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Test"))
            policy.WithOrigins("http://localhost", "https://localhost").AllowAnyMethod().AllowAnyHeader();
        else
            throw new InvalidOperationException("Cors:Origins não configurado. Defina ao menos uma origem permitida.");
    });
});

// ── Controllers + Filters ──────────────────────────────────────────────────────
builder.Services.AddControllers(options =>
{
    options.Filters.Add<GlobalExceptionFilter>();
})
.AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var erros = context.ModelState
            .Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value!.Errors.Select(e => e.ErrorMessage).ToArray());

        var resultado = new ValidationProblemDetails(context.ModelState)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Dados inválidos",
            Detail = "Um ou mais campos não passaram na validação.",
        };
        resultado.Extensions["erros"] = erros;

        return new BadRequestObjectResult(resultado);
    };
});

// ── Health Checks: liveness sem dependências; readiness com banco e broker ─────
var healthChecks = builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("postgres", tags: ["ready"]);
if (builder.Configuration.GetValue<bool>("RabbitMq:Enabled"))
    healthChecks.AddCheck<ConsumidorSagaHealthCheck>("rabbitmq", tags: ["ready"]);

// ── Swagger ────────────────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Oficina Mecânica — OS API",
        Version = "v1",
        Description = "Serviço OS da Fase 4: cadastros, autenticação de funcionários, abertura, status e histórico da ordem de serviço."
    });

    options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, $"{typeof(Program).Assembly.GetName().Name}.xml"));

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JWT de funcionário (login) ou de cliente (Lambda de CPF)."
    });

    options.AddSecurityDefinition(ApiKeyInternaHandler.Esquema, new OpenApiSecurityScheme
    {
        Name = ApiKeyInternaHandler.Cabecalho,
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Description = "Chave de API interna, usada só por /os/interno/*."
    });

    // Requisitos alternativos: cada endpoint aceita um dos dois esquemas, conforme o [Authorize] do controller.
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }, Array.Empty<string>() }
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = ApiKeyInternaHandler.Esquema } }, Array.Empty<string>() }
    });
});

var app = builder.Build();

// ── Migrations + seed de demonstração (ADR-015: banco vazio, sem migração de dados) ─
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
    await SeedDemonstracao.ExecutarAsync(db, tokenService, app.Configuration["Seed:SenhaUsuarios"] ?? string.Empty);
}

// ── Middleware pipeline ────────────────────────────────────────────────────────
app.UseMiddleware<CorrelationIdMiddleware>();

app.UseSwagger(c => c.RouteTemplate = "os/swagger/{documentName}/swagger.json");
app.UseSwaggerUI(c =>
{
    c.RoutePrefix = "os/swagger";
    c.SwaggerEndpoint("/os/swagger/v1/swagger.json", "OS API v1");
});

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/os/health", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/os/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.MapPrometheusScrapingEndpoint();
app.MapControllers();

await app.RunAsync();

public partial class Program { protected Program() { } }
