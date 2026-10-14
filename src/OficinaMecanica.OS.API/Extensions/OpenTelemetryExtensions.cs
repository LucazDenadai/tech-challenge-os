using System.Diagnostics;
using OficinaMecanica.OS.Application.UseCases.Mensageria;
using OficinaMecanica.OS.Infrastructure.Adapters.In.Messaging;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace OficinaMecanica.OS.API.Extensions;

public static class OpenTelemetryExtensions
{
    public static IHostApplicationBuilder AddOpenTelemetry(
        this IHostApplicationBuilder builder, string serviceName)
    {
        var jaegerEndpoint = builder.Configuration["Jaeger:Endpoint"] ?? "http://jaeger:4318";
        // Versão da imagem (sha-<commit>), gravada no build; "local" fora do CI.
        var versao = builder.Configuration["Servico:Versao"] ?? "local";

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(recurso => recurso
                .AddService(serviceName, serviceNamespace: "oficina-mecanica", serviceVersion: versao)
                .AddAttributes([new("deployment.environment", builder.Environment.EnvironmentName)]))
            .WithTracing(tracing => tracing
                .SetSampler(new ParentBasedSampler(new AmostradorInicioDeFluxo()))
                .AddAspNetCoreInstrumentation(o =>
                {
                    o.RecordException = true;
                    o.Filter = contexto => AmostradorInicioDeFluxo.DeveRastrear(contexto.Request.Path);
                })
                .AddEntityFrameworkCoreInstrumentation()
                .AddSource(TelemetriaMensageria.NomeFonte)
                .AddOtlpExporter(o =>
                {
                    o.Endpoint = new Uri($"{jaegerEndpoint.TrimEnd('/')}/v1/traces");
                    o.Protocol = OtlpExportProtocol.HttpProtobuf;
                }))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(TelemetriaMensageria.NomeFonte)
                .AddMeter(MetricasSaga.NomeMedidor)
                // Uma Saga dura de segundos (teste) a horas (pagamento e reparo).
                .AddView(MetricasSaga.NomeDuracao, new ExplicitBucketHistogramConfiguration
                {
                    Boundaries = [5, 30, 60, 300, 900, 1800, 3600, 7200, 14400, 28800, 86400]
                })
                .AddPrometheusExporter());

        return builder;
    }
}

// Só começa um trace o que começa um fluxo: requisição HTTP recebida, mensagem consumida ou publicada (CARD-42a).
// Um SQL sem pai (laço da outbox, verificação de prazos) não vira trace próprio; dentro de um fluxo, continua como
// span filho, porque o ParentBasedSampler segue a decisão do pai.
public sealed class AmostradorInicioDeFluxo : Sampler
{
    public override SamplingResult ShouldSample(in SamplingParameters samplingParameters)
        => new(samplingParameters.Kind is ActivityKind.Server or ActivityKind.Consumer or ActivityKind.Producer
            ? SamplingDecision.RecordAndSample
            : SamplingDecision.Drop);

    // Probes do Kubernetes e scrape do Prometheus não são fluxo de negócio.
    public static bool DeveRastrear(PathString caminho)
        => !(caminho.Value?.EndsWith("/health", StringComparison.OrdinalIgnoreCase) == true
             || caminho.Value?.EndsWith("/ready", StringComparison.OrdinalIgnoreCase) == true
             || caminho.StartsWithSegments("/metrics"));
}
