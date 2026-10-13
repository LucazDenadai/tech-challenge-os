using System.Net;
using System.Net.Http.Json;
using OficinaMecanica.OS.IntegrationTests.Fixtures;

namespace OficinaMecanica.OS.IntegrationTests.Mensageria;

// Compensações, cancelamento, serviço sem peça e prazos pelo broker real (CARD-40b, ADR-017 e ADR-020).
[Collection(PostgresCollection.Nome)]
public class SagaCompensacaoFluxoTests(PostgresFixture postgres) : SagaBrokerTeste(postgres)
{
    private readonly Guid _peca = Guid.NewGuid();
    private readonly Guid _execucao = Guid.NewGuid();
    private readonly Guid _quote = Guid.NewGuid();
    private readonly Guid _reserva = Guid.NewGuid();
    private readonly Guid _pagamento = Guid.NewGuid();

    // Abre a OS e leva a Saga até AwaitingPayment com uma peça reservada, consumindo os comandos do caminho.
    private async Task<SagaAberta> AteAguardarPagamentoAsync()
    {
        var saga = await CenarioSaga.AbrirOsAsync(Factory);
        await ReceberAsync("saga-os.diagnosis-requested.v1");
        await DiagnosticarAsync(saga, _execucao, (_peca, "Peca", 1, 100m));
        await ReceberAsync("saga-os.quote-requested.v1");
        await OrcarAsync(saga, _quote, 100m);
        await AguardarEstadoAsync(saga, "AwaitingApproval");
        await AprovarOrcamentoAsync(saga, _quote);
        await ReceberAsync("saga-os.inventory-reservation-requested.v1");
        await ReservarAsync(saga, _reserva, _peca, 1);
        await ReceberAsync("saga-os.payment-creation-requested.v1");
        await PagamentoAsync(saga, "PaymentPending", _pagamento);
        await AguardarEstadoAsync(saga, "AwaitingPayment");
        return saga;
    }

    private async Task<SagaAberta> AteEmExecucaoAsync()
    {
        var saga = await AteAguardarPagamentoAsync();
        await PagamentoAsync(saga, "PaymentApproved", _pagamento, 100m);
        await ReceberAsync("saga-os.execution-start-requested.v1");
        await ExecucaoAsync(saga, "ExecutionStarted", _execucao);
        await AguardarEstadoAsync(saga, "InExecution");
        return saga;
    }

    [Fact]
    public async Task PagamentoRecusado_LiberaAReservaECancelaAOs()
    {
        var saga = await AteAguardarPagamentoAsync();

        await PagamentoAsync(saga, "PaymentDeclined", _pagamento);

        var liberacao = await ReceberAsync("saga-os.inventory-release-requested.v1");
        Assert.Equal(_reserva.ToString(), liberacao["reservationId"]!.GetValue<string>());
        Assert.Equal($"os:{saga.OsId}:release", liberacao["idempotencyKey"]!.GetValue<string>());
        await AguardarEstadoAsync(saga, "ReleasingReservation");

        await LiberadaAsync(saga, _reserva);
        await AguardarEstadoAsync(saga, "Cancelled");
        Assert.Equal("Cancelada", await StatusDaOsAsync(saga));
        Assert.Equal(0u, await ContarAsync("saga-os.payment-refund-requested.v1"));
    }

    [Fact]
    public async Task PagamentoDesconhecido_MantemAReservaSemComando()
    {
        var saga = await AteAguardarPagamentoAsync();

        await PagamentoAsync(saga, "PaymentOutcomeUnknown", _pagamento);

        await AguardarEstadoAsync(saga, "ReconcilingPayment");
        await Task.Delay(1000);
        Assert.Equal(0u, await ContarAsync("saga-os.inventory-release-requested.v1"));
        Assert.Equal(0u, await ContarAsync("saga-os.payment-creation-requested.v1"));
        Assert.Equal("AguardandoPagamento", await StatusDaOsAsync(saga));
    }

    [Fact]
    public async Task ExecucaoFalhou_EstornaELiberaESoCompensaComAsDuasConfirmacoes()
    {
        var saga = await AteEmExecucaoAsync();

        await ExecucaoAsync(saga, "ExecutionFailed", _execucao);

        var estorno = await ReceberAsync("saga-os.payment-refund-requested.v1");
        Assert.Equal(_pagamento.ToString(), estorno["paymentId"]!.GetValue<string>());
        Assert.Equal(100m, estorno["amount"]!.GetValue<decimal>());
        await ReceberAsync("saga-os.inventory-release-requested.v1");

        await PagamentoAsync(saga, "PaymentRefunded", _pagamento);
        await Task.Delay(500);
        Assert.Equal("Compensating", (await AguardarEstadoAsync(saga, "Compensating")).GetProperty("estado").GetString());

        await LiberadaAsync(saga, _reserva);
        await AguardarEstadoAsync(saga, "Compensated");
        Assert.Equal("Cancelada", await StatusDaOsAsync(saga));
    }

    [Fact]
    public async Task CancelamentoNaFila_Responde202EAposExecucaoCanceladaCompensa()
    {
        var saga = await AteEmExecucaoAsync();
        var http = await Factory.ClienteFuncionarioAsync(CenarioSaga.Atendente);

        var resposta = await http.PostAsync($"/os/ordens-servico/{saga.OsId}/cancelamento", null);

        Assert.Equal(HttpStatusCode.Accepted, resposta.StatusCode);
        var pedido = await ReceberAsync("saga-os.execution-cancel-requested.v1");
        Assert.Equal(_execucao.ToString(), pedido["executionId"]!.GetValue<string>());
        Assert.Equal("EmExecucao", await StatusDaOsAsync(saga));

        await ExecucaoAsync(saga, "ExecutionCancelled", _execucao);
        await ReceberAsync("saga-os.payment-refund-requested.v1");
        await ReceberAsync("saga-os.inventory-release-requested.v1");
        await PagamentoAsync(saga, "PaymentRefunded", _pagamento);
        await LiberadaAsync(saga, _reserva);

        await AguardarEstadoAsync(saga, "Compensated");
        Assert.Equal("Cancelada", await StatusDaOsAsync(saga));
    }

    [Fact]
    public async Task CancelamentoComReparoEmAndamento_RecusadoPorOperacoesSemEstorno()
    {
        var saga = await AteEmExecucaoAsync();
        var http = await Factory.ClienteFuncionarioAsync(CenarioSaga.Atendente);
        await http.PostAsync($"/os/ordens-servico/{saga.OsId}/cancelamento", null);
        await ReceberAsync("saga-os.execution-cancel-requested.v1");

        await ExecucaoAsync(saga, "ExecutionCancelRejected", _execucao);

        var final = await AguardarEstadoAsync(saga, "InExecution");
        Assert.Contains("EmReparo", final.GetProperty("motivo").GetString());
        await Task.Delay(500);
        Assert.Equal(0u, await ContarAsync("saga-os.payment-refund-requested.v1"));
    }

    [Fact]
    public async Task ConclusaoProcessadaAntesDoInicio_TerminaEmCompleted()
    {
        // ExecutionStarted e ExecutionCompleted vão por filas diferentes; a ordem de processamento não é garantida.
        var saga = await AteAguardarPagamentoAsync();
        await PagamentoAsync(saga, "PaymentApproved", _pagamento, 100m);
        await ReceberAsync("saga-os.execution-start-requested.v1");
        await AguardarEstadoAsync(saga, "StartingExecution");

        await ExecucaoAsync(saga, "ExecutionCompleted", _execucao);
        await AguardarEstadoAsync(saga, "Completed");
        await ExecucaoAsync(saga, "ExecutionStarted", _execucao);

        await Task.Delay(500);
        Assert.Equal("Completed", (await AguardarEstadoAsync(saga, "Completed")).GetProperty("estado").GetString());
        Assert.Equal("Finalizada", await StatusDaOsAsync(saga));
    }

    [Fact]
    public async Task SoServico_ConcluiSemReservaEInicioSemReservationId()
    {
        var saga = await CenarioSaga.AbrirOsAsync(Factory);
        await ReceberAsync("saga-os.diagnosis-requested.v1");
        await DiagnosticarAsync(saga, _execucao, (Guid.NewGuid(), "Servico", 1, 80m));
        await ReceberAsync("saga-os.quote-requested.v1");
        await OrcarAsync(saga, _quote, 80m);
        await AguardarEstadoAsync(saga, "AwaitingApproval");

        await AprovarOrcamentoAsync(saga, _quote);

        var pedidoPagamento = await ReceberAsync("saga-os.payment-creation-requested.v1");
        Assert.Equal(80m, pedidoPagamento["amount"]!.GetValue<decimal>());
        Assert.Equal(0u, await ContarAsync("saga-os.inventory-reservation-requested.v1"));
        await PagamentoAsync(saga, "PaymentApproved", _pagamento, 80m);
        var inicio = await ReceberAsync("saga-os.execution-start-requested.v1");
        Assert.Null(inicio["reservationId"]);
        await ExecucaoAsync(saga, "ExecutionStarted", _execucao);
        await ExecucaoAsync(saga, "ExecutionCompleted", _execucao);

        await AguardarEstadoAsync(saga, "Completed");
        Assert.Equal("Finalizada", await StatusDaOsAsync(saga));
    }

    [Fact]
    public async Task PrazoEsgotado_ReenviaComAMesmaChaveEDepoisAdminRetoma()
    {
        await ReiniciarOsAsync(new Dictionary<string, string?>
        {
            ["Saga:PrazoRespostaMinutos"] = "0.03",
            ["Saga:MaxReenvios"] = "1",
            ["Saga:IntervaloVerificacaoSegundos"] = "1"
        });
        var saga = await CenarioSaga.AbrirOsAsync(Factory);
        await ReceberAsync("saga-os.diagnosis-requested.v1");
        await DiagnosticarAsync(saga, _execucao, (_peca, "Peca", 1, 100m));
        var primeiro = await ReceberAsync("saga-os.quote-requested.v1");

        // Billing não responde: um reenvio com a mesma chave, depois ação manual.
        var reenviado = await ReceberAsync("saga-os.quote-requested.v1");
        Assert.NotEqual(primeiro["messageId"]!.GetValue<string>(), reenviado["messageId"]!.GetValue<string>());
        Assert.Equal(primeiro["idempotencyKey"]!.GetValue<string>(), reenviado["idempotencyKey"]!.GetValue<string>());
        await AguardarEstadoAsync(saga, "ManualActionRequired");

        // Atendente não resolve; Admin retoma com motivo.
        var atendente = await Factory.ClienteFuncionarioAsync(CenarioSaga.Atendente);
        var negado = await atendente.PostAsJsonAsync($"/os/ordens-servico/{saga.OsId}/saga/resolucao", new { acao = "Retomar", motivo = "Billing voltou" });
        Assert.Equal(HttpStatusCode.Forbidden, negado.StatusCode);
        var admin = await Factory.ClienteFuncionarioAsync("admin@oficina.example");
        var resolucao = await admin.PostAsJsonAsync($"/os/ordens-servico/{saga.OsId}/saga/resolucao", new { acao = "Retomar", motivo = "Billing voltou" });
        Assert.Equal(HttpStatusCode.NoContent, resolucao.StatusCode);

        var retomado = await ReceberAsync("saga-os.quote-requested.v1");
        Assert.Equal(primeiro["idempotencyKey"]!.GetValue<string>(), retomado["idempotencyKey"]!.GetValue<string>());
        var estado = await AguardarEstadoAsync(saga, "RequestingQuote");
        var ultima = estado.GetProperty("transicoes").EnumerateArray().Last(t => t.GetProperty("causa").GetString() == "ResolucaoAdmin");
        Assert.Contains("admin@oficina.example", ultima.GetProperty("motivo").GetString());
    }

    [Fact]
    public async Task CancelarEmAcaoManual_Recusado422()
    {
        var saga = await AteAguardarPagamentoAsync();
        await PagamentoAsync(saga, "PaymentApproved", _pagamento, 1m);
        await AguardarEstadoAsync(saga, "ManualActionRequired");
        var http = await Factory.ClienteFuncionarioAsync(CenarioSaga.Atendente);

        var resposta = await http.PostAsync($"/os/ordens-servico/{saga.OsId}/cancelamento", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
        // Pagamento divergente: Admin só pode cancelar, e a Saga estorna e libera.
        var admin = await Factory.ClienteFuncionarioAsync("admin@oficina.example");
        var retomar = await admin.PostAsJsonAsync($"/os/ordens-servico/{saga.OsId}/saga/resolucao", new { acao = "Retomar", motivo = "x" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, retomar.StatusCode);
        var cancelar = await admin.PostAsJsonAsync($"/os/ordens-servico/{saga.OsId}/saga/resolucao", new { acao = "Cancelar", motivo = "Valor divergente" });
        Assert.Equal(HttpStatusCode.NoContent, cancelar.StatusCode);
        await ReceberAsync("saga-os.payment-refund-requested.v1");
        await ReceberAsync("saga-os.inventory-release-requested.v1");
        await AguardarEstadoAsync(saga, "Compensating");
    }
}
