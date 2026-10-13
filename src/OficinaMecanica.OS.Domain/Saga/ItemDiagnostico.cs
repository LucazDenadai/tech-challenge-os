using System.Diagnostics.CodeAnalysis;

namespace OficinaMecanica.OS.Domain.Saga;

// Item do diagnóstico de Operações com o preço do catálogo no momento (snapshot, emenda do ADR-015).
// Guardado na Saga para pedir o orçamento e a reserva sem consultar Operações de novo.
public class ItemDiagnostico
{
    public Guid ItemId { get; private set; }
    public TipoItemDiagnostico Tipo { get; private set; }
    public string Descricao { get; private set; } = string.Empty;
    public int Quantidade { get; private set; }
    public decimal PrecoUnitario { get; private set; }

    [ExcludeFromCodeCoverage]
    protected ItemDiagnostico() { }

    public ItemDiagnostico(Guid itemId, TipoItemDiagnostico tipo, string descricao, int quantidade, decimal precoUnitario)
    {
        if (itemId == Guid.Empty)
            throw new ArgumentException("O item do diagnóstico é obrigatório.", nameof(itemId));
        if (quantidade < 1)
            throw new ArgumentException("A quantidade deve ser maior ou igual a 1.", nameof(quantidade));

        ItemId = itemId;
        Tipo = tipo;
        Descricao = descricao;
        Quantidade = quantidade;
        PrecoUnitario = precoUnitario;
    }
}

// Registro de cada transição, com a mensagem que a causou, para consulta e auditoria.
public class TransicaoSaga
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid SagaId { get; private set; }
    public EstadoSaga? EstadoAnterior { get; private set; }
    public EstadoSaga EstadoNovo { get; private set; }
    public string Causa { get; private set; } = string.Empty;
    public Guid? MessageId { get; private set; }
    public string? Motivo { get; private set; }
    public DateTime Em { get; private set; } = DateTime.UtcNow;

    [ExcludeFromCodeCoverage]
    protected TransicaoSaga() { }

    internal TransicaoSaga(Guid sagaId, EstadoSaga? anterior, EstadoSaga novo, string causa, Guid? messageId, string? motivo)
    {
        SagaId = sagaId;
        EstadoAnterior = anterior;
        EstadoNovo = novo;
        Causa = causa;
        MessageId = messageId;
        Motivo = motivo;
    }
}
