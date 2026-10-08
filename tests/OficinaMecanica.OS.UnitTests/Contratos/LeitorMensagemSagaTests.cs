using System.Text;
using OficinaMecanica.OS.Application.Contratos.Saga;

namespace OficinaMecanica.OS.UnitTests.Contratos;

public class LeitorMensagemSagaTests
{
    private static readonly CanalConsumido _pagamentoAprovado = CatalogoCanaisOS.Obter("saga-os.payment-approved.v1")!;

    [Fact]
    public void Ler_MensagemValida_DevolveDtoTipado()
    {
        var exemplo = MensagensExemplo.Criar(_pagamentoAprovado);

        var leitura = LeitorMensagemSaga.Ler(_pagamentoAprovado, MensagensExemplo.Bytes(exemplo));

        var mensagem = Assert.IsType<PaymentApproved>(leitura.Mensagem);
        Assert.Equal(245.90m, mensagem.Amount);
        Assert.Equal(Guid.Parse(exemplo["correlationId"]!.GetValue<string>()), mensagem.CorrelationId);
    }

    [Fact]
    public void Ler_MessageTypeDeOutroCanal_RejeitaComMotivoEPreservaIdentificadores()
    {
        var exemplo = MensagensExemplo.Criar(_pagamentoAprovado);
        exemplo["messageType"] = "PaymentDeclined.v1";

        var leitura = LeitorMensagemSaga.Ler(_pagamentoAprovado, MensagensExemplo.Bytes(exemplo));

        Assert.False(leitura.Valida);
        Assert.Contains("não corresponde ao canal", leitura.Motivo);
        Assert.Equal(Guid.Parse(exemplo["messageId"]!.GetValue<string>()), leitura.MessageId);
        Assert.Equal(Guid.Parse(exemplo["correlationId"]!.GetValue<string>()), leitura.CorrelationId);
    }

    [Fact]
    public void Ler_SchemaVersionNaoSuportado_Rejeita()
    {
        // schemaVersion 2 é válido no schema (mínimo 1), mas o canal v1 só aceita a versão 1.
        var exemplo = MensagensExemplo.Criar(_pagamentoAprovado);
        exemplo["schemaVersion"] = 2;

        var leitura = LeitorMensagemSaga.Ler(_pagamentoAprovado, MensagensExemplo.Bytes(exemplo));

        Assert.False(leitura.Valida);
        Assert.Contains("schemaVersion 2 não suportado", leitura.Motivo);
    }

    [Fact]
    public void Ler_ProdutorDiferenteDoCanal_Rejeita()
    {
        // "operacoes" é um producer válido no enum, mas o canal de pagamento é publicado só pelo Billing.
        var exemplo = MensagensExemplo.Criar(_pagamentoAprovado);
        exemplo["producer"] = "operacoes";

        var leitura = LeitorMensagemSaga.Ler(_pagamentoAprovado, MensagensExemplo.Bytes(exemplo));

        Assert.False(leitura.Valida);
        Assert.Contains("não é o produtor do canal", leitura.Motivo);
    }

    [Theory]
    [InlineData("isto não é json")]
    [InlineData("[1, 2, 3]")]
    [InlineData("")]
    public void Ler_CorpoQueNaoEObjetoJson_RejeitaSemIdentificadores(string corpo)
    {
        var leitura = LeitorMensagemSaga.Ler(_pagamentoAprovado, Encoding.UTF8.GetBytes(corpo));

        Assert.False(leitura.Valida);
        Assert.NotNull(leitura.Motivo);
        Assert.Null(leitura.MessageId);
    }

    [Fact]
    public void Ler_CampoNuloEmPropriedadeObrigatoria_Rejeita()
    {
        var exemplo = MensagensExemplo.Criar(_pagamentoAprovado);
        exemplo["currency"] = null;

        var leitura = LeitorMensagemSaga.Ler(_pagamentoAprovado, MensagensExemplo.Bytes(exemplo));

        Assert.False(leitura.Valida);
        Assert.Contains("Payload fora do contrato", leitura.Motivo);
    }
}
