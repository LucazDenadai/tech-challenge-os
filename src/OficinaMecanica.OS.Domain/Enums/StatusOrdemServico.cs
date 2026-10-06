namespace OficinaMecanica.OS.Domain.Enums;

// Mapeamento do ADR-017: status de negócio derivado do estado da Saga.
public enum StatusOrdemServico
{
    EmDiagnostico = 1,
    AguardandoAprovacao = 2,
    AguardandoPagamento = 3,
    EmExecucao = 4,
    Finalizada = 5,
    Entregue = 6,
    Cancelada = 7
}
