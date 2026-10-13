using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OficinaMecanica.E2E.Suporte;
using Reqnroll;
using Xunit;

namespace OficinaMecanica.E2E.Steps;

// Steps da Saga da OS. Cada step usa só as APIs públicas dos três serviços, com o papel de quem faria a ação:
// atendente no OS, mecânico em Operações, cliente e Admin no Billing. Nenhum banco é lido diretamente.
[Binding]
public class SagaOrdemServicoSteps
{
    private const string CodigoOleo = "OLEO-5W30-1L";
    private const string NomeServico = "Troca de óleo e filtro";

    private Guid _clienteId, _veiculoId, _filialId, _oleoId, _servicoId;
    private int _estoqueOleoInicial;
    private Guid _osId;
    private Guid _execucaoId;

    // ── Dado ───────────────────────────────────────────────────────────────────

    [Given("que cliente e veículo estão aptos na filial selecionada")]
    public async Task ClienteEVeiculoAptos()
    {
        var atendente = await Ambiente.FuncionarioAsync(Ambiente.OsUrl, Ambiente.Atendente);
        var cliente = (await atendente.GetFromJsonAsync<JsonElement>($"/os/clientes?busca={Ambiente.CpfCliente}")).EnumerateArray().Single();
        _clienteId = cliente.GetProperty("id").GetGuid();
        Assert.False(string.IsNullOrWhiteSpace(cliente.GetProperty("email").GetString()));
        _veiculoId = (await atendente.GetFromJsonAsync<JsonElement>($"/os/veiculos/cliente/{_clienteId}")).EnumerateArray().First().GetProperty("id").GetGuid();
        _filialId = (await atendente.GetFromJsonAsync<JsonElement>("/os/filiais")).EnumerateArray().Single().GetProperty("id").GetGuid();

        // A filial também precisa operar estoque em Operações (mesmo Id nos dois seeds).
        var mecanico = await Ambiente.FuncionarioAsync(Ambiente.OperacoesUrl, Ambiente.Mecanico);
        var filiais = await mecanico.GetFromJsonAsync<JsonElement>("/operacoes/estoque/filiais");
        Assert.Contains(filiais.EnumerateArray(), f => f.GetProperty("id").GetGuid() == _filialId);
        _oleoId = (await mecanico.GetFromJsonAsync<JsonElement>("/operacoes/pecas")).EnumerateArray()
            .Single(p => p.GetProperty("codigo").GetString() == CodigoOleo).GetProperty("id").GetGuid();
        _servicoId = (await mecanico.GetFromJsonAsync<JsonElement>("/operacoes/servicos")).EnumerateArray()
            .Single(s => s.GetProperty("nome").GetString() == NomeServico).GetProperty("id").GetGuid();
        _estoqueOleoInicial = await EstoqueOleoAsync();
    }

    // ── Quando ─────────────────────────────────────────────────────────────────

    [When("a OS é aberta")]
    public async Task AOsEAberta()
    {
        var atendente = await Ambiente.FuncionarioAsync(Ambiente.OsUrl, Ambiente.Atendente);
        var resposta = await atendente.PostAsJsonAsync("/os/ordens-servico", new { clienteId = _clienteId, veiculoId = _veiculoId, filialId = _filialId, observacoes = "BDD ponta a ponta" });
        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        _osId = (await resposta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // DiagnosisRequested chega a Operações, que abre a execução da OS.
        var execucao = await Ambiente.AguardarAsync(() => ExecucaoAsync(), Existe, "Operações abrir a execução");
        _execucaoId = execucao.GetProperty("id").GetGuid();
    }

    [When("o mecânico diagnostica {int} unidades de óleo e a troca de óleo")]
    public async Task OMecanicoDiagnostica(int unidades)
    {
        var mecanico = await Ambiente.FuncionarioAsync(Ambiente.OperacoesUrl, Ambiente.Mecanico);
        var resposta = await mecanico.PostAsJsonAsync($"/operacoes/execucoes/{_execucaoId}/diagnostico", new
        {
            itens = new object[]
            {
                new { tipo = "Peca", itemId = _oleoId, quantidade = unidades },
                new { tipo = "Servico", itemId = _servicoId, quantidade = 1 }
            }
        });
        Assert.True(resposta.IsSuccessStatusCode, await resposta.Content.ReadAsStringAsync());

        // DiagnosisCompleted → OS pede o orçamento → Billing publica QuoteReady.
        await AguardarSagaAsync("AwaitingApproval");
    }

    [When("o cliente aprova o orçamento")]
    public Task OClienteAprova() => DecidirOrcamentoAsync("aprovar");

    [When("o cliente recusa o orçamento")]
    public Task OClienteRecusa() => DecidirOrcamentoAsync("recusar");

    [When("o Mercado Pago confirma o pagamento")]
    public Task OMercadoPagoConfirma() => SimularPagamentoAsync("approved");

    [When("o Mercado Pago recusa o pagamento")]
    public Task OMercadoPagoRecusa() => SimularPagamentoAsync("rejected");

    [When("Operações inicia e conclui o reparo consumindo {int} unidades de óleo")]
    public async Task OperacoesIniciaEConclui(int unidades)
    {
        var mecanico = await IniciarReparoAsync();
        var resposta = await mecanico.PostAsJsonAsync($"/operacoes/execucoes/{_execucaoId}/conclusao",
            new { pecasConsumidas = new[] { new { pecaId = _oleoId, quantidade = unidades } } });
        Assert.True(resposta.IsSuccessStatusCode, await resposta.Content.ReadAsStringAsync());
    }

    [When("Operações inicia o reparo e registra falha sem consumir peças")]
    public async Task OperacoesFalha()
    {
        var mecanico = await IniciarReparoAsync();
        var resposta = await mecanico.PostAsJsonAsync($"/operacoes/execucoes/{_execucaoId}/falha",
            new { motivo = "Peça incompatível com o veículo", pecasConsumidas = Array.Empty<object>() });
        Assert.True(resposta.IsSuccessStatusCode, await resposta.Content.ReadAsStringAsync());
    }

    // ── Então ──────────────────────────────────────────────────────────────────

    [Then("a Saga termina no estado {string}")]
    public async Task ASagaTermina(string estado)
    {
        var saga = await AguardarSagaAsync(estado);
        Assert.True(saga.GetProperty("terminada").GetBoolean());
    }

    [Then("a OS apresenta o status {string}")]
    public async Task AOsApresentaStatus(string status)
        => Assert.Equal(status, (await StatusOsAsync()).GetProperty("status").GetString());

    [Then("a OS apresenta o status {string} com o histórico do fluxo")]
    public async Task AOsApresentaStatusEHistorico(string status)
    {
        await AOsApresentaStatus(status);
        var atendente = await Ambiente.FuncionarioAsync(Ambiente.OsUrl, Ambiente.Atendente);
        var historico = await atendente.GetFromJsonAsync<JsonElement>($"/os/ordens-servico/{_osId}/historico");
        Assert.Equal(
            ["AguardandoAprovacao", "AguardandoPagamento", "EmExecucao", "Finalizada"],
            historico.EnumerateArray().Select(h => h.GetProperty("statusNovo").GetString()));
    }

    [Then("Billing mantém o pagamento {string}")]
    public async Task BillingMantemPagamento(string status)
    {
        var pagamento = await Ambiente.AguardarAsync(PagamentoAsync, p => Existe(p) && p.GetProperty("status").GetString() == status,
            $"pagamento {status} no Billing");
        Assert.Equal(_osId, pagamento.GetProperty("osId").GetGuid());
    }

    [Then("nenhuma cobrança é criada")]
    public async Task NenhumaCobranca()
    {
        var admin = await Ambiente.FuncionarioAsync(Ambiente.BillingUrl, Ambiente.Admin);
        Assert.Empty((await admin.GetFromJsonAsync<JsonElement>($"/billing/pagamentos/os/{_osId}")).EnumerateArray());
    }

    [Then("Operações mantém a execução {string}")]
    public async Task OperacoesMantemExecucao(string status)
        => Assert.Equal(status, (await ExecucaoAsync()).GetProperty("status").GetString());

    [Then("Operações mantém a execução {string} e o estoque de óleo baixa {int} unidades")]
    public async Task OperacoesMantemExecucaoEBaixa(string status, int unidades)
    {
        await OperacoesMantemExecucao(status);
        Assert.Equal(_estoqueOleoInicial - unidades, await EstoqueOleoAsync());
    }

    [Then("o estoque de óleo da filial não muda")]
    public async Task EstoqueNaoMuda()
    {
        // Reserva liberada e disponível de volta ao inicial: nada ficou preso nem foi consumido.
        var saldo = await Ambiente.AguardarAsync(SaldoOleoAsync, s => s.GetProperty("quantidadeReservada").GetInt32() == 0
            && s.GetProperty("quantidadeDisponivel").GetInt32() == _estoqueOleoInicial, "estoque de óleo voltar ao inicial");
        Assert.Equal(_estoqueOleoInicial, saldo.GetProperty("quantidadeDisponivel").GetInt32());
    }

    // ── Auxiliares ─────────────────────────────────────────────────────────────

    // JsonElement é struct: "não encontrado" é o valor padrão (Undefined), não null.
    private static bool Existe(JsonElement e) => e.ValueKind == JsonValueKind.Object;

    private async Task DecidirOrcamentoAsync(string decisao)
    {
        var cliente = Ambiente.ClienteFinal(Ambiente.BillingUrl, _clienteId);
        var orcamento = (await cliente.GetFromJsonAsync<JsonElement>("/billing/orcamentos/meus")).EnumerateArray()
            .Single(o => o.GetProperty("osId").GetGuid() == _osId);
        var resposta = await cliente.PostAsJsonAsync($"/billing/orcamentos/{orcamento.GetProperty("quoteId").GetGuid()}/{decisao}",
            new { versao = orcamento.GetProperty("versaoVigente").GetProperty("versao").GetInt32() });
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    }

    private async Task SimularPagamentoAsync(string status)
    {
        // A cobrança nasce depois da reserva (InventoryReserved → PaymentCreationRequested).
        var pagamento = await Ambiente.AguardarAsync(PagamentoAsync,
            p => Existe(p) && p.TryGetProperty("referenciaProvedor", out var r) && r.ValueKind == JsonValueKind.String, "cobrança criada no Billing");
        var admin = await Ambiente.FuncionarioAsync(Ambiente.BillingUrl, Ambiente.Admin);
        var resposta = await admin.PostAsJsonAsync($"/billing/simulador/pagamentos/{pagamento.GetProperty("referenciaProvedor").GetString()}/status", new { status });
        Assert.True(resposta.IsSuccessStatusCode, await resposta.Content.ReadAsStringAsync());
    }

    private async Task<HttpClient> IniciarReparoAsync()
    {
        // PaymentApproved → OS pede o início → a execução entra na fila.
        await Ambiente.AguardarAsync(() => ExecucaoAsync(), e => Existe(e) && e.GetProperty("status").GetString() == "NaFila", "execução na fila");
        var mecanico = await Ambiente.FuncionarioAsync(Ambiente.OperacoesUrl, Ambiente.Mecanico);
        var resposta = await mecanico.PostAsync($"/operacoes/execucoes/{_execucaoId}/reparo", null);
        Assert.True(resposta.IsSuccessStatusCode, await resposta.Content.ReadAsStringAsync());
        return mecanico;
    }

    private async Task<JsonElement> AguardarSagaAsync(string estado)
    {
        var atendente = await Ambiente.FuncionarioAsync(Ambiente.OsUrl, Ambiente.Atendente);
        return await Ambiente.AguardarAsync(
            () => atendente.GetFromJsonAsync<JsonElement>($"/os/ordens-servico/{_osId}/saga"),
            s => s.GetProperty("estado").GetString() == estado, $"Saga em {estado}");
    }

    private async Task<JsonElement> StatusOsAsync()
    {
        var atendente = await Ambiente.FuncionarioAsync(Ambiente.OsUrl, Ambiente.Atendente);
        return await atendente.GetFromJsonAsync<JsonElement>($"/os/ordens-servico/{_osId}/status");
    }

    private async Task<JsonElement> ExecucaoAsync()
    {
        var mecanico = await Ambiente.FuncionarioAsync(Ambiente.OperacoesUrl, Ambiente.Mecanico);
        var resposta = await mecanico.GetAsync($"/operacoes/execucoes/os/{_osId}");
        return resposta.StatusCode == HttpStatusCode.NotFound ? default : await resposta.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<JsonElement> PagamentoAsync()
    {
        var admin = await Ambiente.FuncionarioAsync(Ambiente.BillingUrl, Ambiente.Admin);
        var pagamentos = await admin.GetFromJsonAsync<JsonElement>($"/billing/pagamentos/os/{_osId}");
        return pagamentos.EnumerateArray().FirstOrDefault();
    }

    private async Task<JsonElement> SaldoOleoAsync()
    {
        var mecanico = await Ambiente.FuncionarioAsync(Ambiente.OperacoesUrl, Ambiente.Mecanico);
        return (await mecanico.GetFromJsonAsync<JsonElement>($"/operacoes/estoque/filiais/{_filialId}/saldos")).EnumerateArray()
            .Single(s => s.GetProperty("pecaId").GetGuid() == _oleoId);
    }

    private async Task<int> EstoqueOleoAsync() => (await SaldoOleoAsync()).GetProperty("quantidadeDisponivel").GetInt32();
}
