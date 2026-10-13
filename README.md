# tech-challenge-os

Serviço **OS** da Fase 4 do Tech Challenge (oficina mecânica). É dono de clientes, veículos, filiais, usuários funcionários e da ordem de serviço, com status e histórico. Também hospeda a Saga orquestrada da OS (ADR-017 e ADR-020): fluxo normal (CARD-40a) e compensações, cancelamento e prazos (CARD-40b).

Decisões e contratos ficam em [tech-challenge-docs](https://github.com/LucazDenadai/tech-challenge-docs): limites e ownership ([ADR-014](https://github.com/LucazDenadai/tech-challenge-docs/blob/main/adr/ADR-014-limites-microsservicos-fase4.md), [ADR-015](https://github.com/LucazDenadai/tech-challenge-docs/blob/main/adr/ADR-015-ownership-e-infraestrutura-fase4.md)), banco ([ADR-016](https://github.com/LucazDenadai/tech-challenge-docs/blob/main/adr/ADR-016-bancos-sql-nosql-fase4.md)), Saga ([ADR-017](https://github.com/LucazDenadai/tech-challenge-docs/blob/main/adr/ADR-017-saga-orquestrada-os-fase4.md)) e contratos assíncronos ([ADR-018](https://github.com/LucazDenadai/tech-challenge-docs/blob/main/adr/ADR-018-contratos-assincronos-asyncapi-fase4.md)).

## Estrutura

```
src/OficinaMecanica.OS.Domain          entidades, regras de transição da OS e agregado da Saga
src/OficinaMecanica.OS.Application     casos de uso, portas e DTOs dos contratos da Saga
src/OficinaMecanica.OS.Infrastructure  EF Core/PostgreSQL, inbox, outbox, consumidor e despachante RabbitMQ, JWT, e-mail
src/OficinaMecanica.OS.API             controllers, autenticação, Swagger, health checks
tests/                                 testes unitários, de contrato e de integração
e2e/                                   BDD ponta a ponta com os três serviços (Reqnroll)
contratos/asyncapi-saga-os.yaml        cópia da spec AsyncAPI usada nos testes de contrato
docs/openapi/os-v1.json                OpenAPI gerado pela API
```

## Configuração

Nenhum segredo é versionado. Em execução, todos chegam por variável de ambiente (`Secao__Chave`) ou Secret do Kubernetes.

| Chave | Uso |
|---|---|
| `ConnectionStrings__DefaultConnection` | PostgreSQL exclusivo do OS. Migrations e seed rodam no start. |
| `Jwt__Key`, `Jwt__Issuer`, `Jwt__Audience` | Assinatura e validação do JWT. Mesma chave da Lambda de CPF, de Billing e de Operações. |
| `Seed__SenhaUsuarios` | Senha dos usuários de demonstração. Sem ela o serviço não sobe. |
| `Interno__ApiKey` | Chave do cabeçalho `X-Api-Key` do endpoint interno da Lambda. |
| `RabbitMq__Enabled`, `__Host`, `__Port`, `__VirtualHost`, `__Username`, `__Password` | Consumidor e despachante da Saga. Habilitado, exige usuário e senha. |
| `RabbitMq__IntervaloOutboxMs` | Intervalo de leitura da outbox (padrão 500 ms). |
| `Saga__PrazoRespostaMinutos`, `__MaxReenvios`, `__IntervaloVerificacaoSegundos`, `__VerificacaoHabilitada` | Prazo das etapas que esperam participante (padrão 5 min), reenvios antes da ação manual (3) e intervalo da verificação (30 s). |
| `Cors__Origins__0` | Origem permitida. Obrigatória fora de `Development`/`Test`. |
| `Jaeger__Endpoint` | Coletor OTLP/HTTP dos traces. |

## Executar

```sh
docker run -d --name os-postgres -e POSTGRES_PASSWORD=postgres -p 5432:5432 postgres:16-alpine

ConnectionStrings__DefaultConnection="Host=localhost;Database=oficina_os;Username=postgres;Password=postgres" \
Jwt__Key="<32+ caracteres>" Seed__SenhaUsuarios="<senha forte>" Interno__ApiKey="<chave>" \
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/OficinaMecanica.OS.API
```

Swagger em `/os/swagger`, liveness em `/os/health` e readiness (banco e, se habilitado, RabbitMQ) em `/os/ready`.

O seed cria a filial `FILIAL-DEMO` com `Id` fixo `378aeb39-37f6-43c1-9526-5b1a9fadd553`, o mesmo do seed de Operações, os usuários `admin@`, `atendente@` e `mecanico@oficina.example` e dois clientes com veículo, sem dados pessoais reais.

## API

| Rota | Quem | O que faz |
|---|---|---|
| `POST /os/auth/login` | público, 10/min por IP | JWT de funcionário. Credencial inválida sempre devolve `401`. |
| `/os/clientes`, `/os/veiculos` | Admin, Atendente (leitura de veículo também Mecânico) | Cadastros. O cliente exige e-mail válido: é o `payerEmail` do Pix. |
| `/os/usuarios` | Admin | Usuários funcionários |
| `GET /os/filiais` | funcionários | Filiais ativas, para o `filialId` da abertura |
| `POST /os/ordens-servico` | Admin, Atendente | Abre a OS em `EmDiagnostico` e inicia a Saga. Cliente sem e-mail válido: `422`. |
| `GET /os/ordens-servico[/{id}[/status\|/historico]]` | funcionários | Lista, detalhe, status e histórico |
| `GET /os/ordens-servico/{id}/saga` | Admin, Atendente | Estado da Saga, referências de orçamento, reserva, pagamento e execução, e transições. Sem dados do cliente. |
| `POST /os/ordens-servico/{id}/entrega` | Admin, Atendente | Finalizada → Entregue, fora da Saga. |
| `POST /os/ordens-servico/{id}/cancelamento` | Admin, Atendente | Pede o cancelamento pela Saga (ADR-020): `204` quando nada precisa ser desfeito; `202` quando a Saga libera, estorna ou pede o cancelamento a Operações. `422` com a OS encerrada ou em ação manual. |
| `POST /os/ordens-servico/{id}/saga/resolucao` | Admin | Resolve `ManualActionRequired`: `{ "acao": "Cancelar" \| "Retomar", "motivo": "..." }`. Motivo e usuário vão para o histórico da Saga. |
| `GET /os/ordens-servico/acompanhar/{numero}` | Cliente (JWT da Lambda) | Acompanhamento só das próprias OS |
| `POST /os/interno/clientes/busca-por-cpf` | `X-Api-Key` | Devolve `clienteId` e `ativo` para a Lambda. Não deve ser exposto no API Gateway. |

Todas as respostas devolvem o cabeçalho `X-Correlation-Id`, recebido ou gerado.

## Mensageria

O serviço consome os 23 canais cujo consumidor é o OS no AsyncAPI e publica os 8 comandos da Saga. Para cada canal consumido `saga-os.<mensagem>.v1` (exchange fanout), declara a fila `os.<canal>` e a DLQ `os.<canal>.dlq`.

### Saga: fluxo normal (CARD-40a)

A abertura grava a OS, a Saga em `Diagnosing` e `DiagnosisRequested` na outbox no mesmo commit. Depois, cada mensagem recebida é aplicada em uma transação do PostgreSQL do OS: inbox, transição da Saga, status e histórico da OS e o próximo comando na outbox.

| Estado | Mensagem | Novo estado | Comando |
|---|---|---|---|
| `Diagnosing` | `DiagnosisCompleted` / `DiagnosisRejected` | `RequestingQuote` / `Cancelled` | `QuoteRequested` / — |
| `RequestingQuote` | `QuoteReady` / `QuoteRejected` | `AwaitingApproval` / `Cancelled` | — |
| `AwaitingApproval` | `QuoteApproved` / `QuoteDeclined`, `QuoteExpired` | `ReservingInventory` (sem peças: `CreatingPayment`) / `Cancelled` | `InventoryReservationRequested` (só peças) ou `PaymentCreationRequested` / — |
| `ReservingInventory` | `InventoryReserved` / `InventoryReservationRejected` | `CreatingPayment` / `Cancelled` | `PaymentCreationRequested` (com `payerEmail`) / — |
| `CreatingPayment` | `PaymentPending` | `AwaitingPayment` | — |
| `CreatingPayment`, `AwaitingPayment` | `PaymentApproved` | `StartingExecution` | `ExecutionStartRequested` |
| `StartingExecution` | `ExecutionStarted` | `InExecution` | — (sem reserva, o início sai sem `reservationId`) |
| `InExecution` | `ExecutionCompleted` | `Completed` | — |

- **Status da OS:** derivado do estado da Saga pela tabela do ADR-017, na mesma transação.
- **`idempotencyKey`:** `os:<osId>:<etapa>` (`diagnosis`, `quote:1`, `reservation`, `payment:<versão>`, `execution`). Reprocessar o mesmo evento gera a mesma chave.
- **Fora do fluxo:** mensagem válida que não se aplica ao estado atual (atrasada, repetida com outro `messageId` ou de estado terminal) é confirmada sem mudar nada, com log de aviso.

### Saga: compensações, cancelamento e prazos (CARD-40b)

| Estado | Mensagem ou ação | Novo estado | Comandos |
|---|---|---|---|
| `CreatingPayment`, `AwaitingPayment` | `PaymentOutcomeUnknown` | `ReconcilingPayment` | — (mantém a reserva) |
| `CreatingPayment`, `AwaitingPayment`, `ReconcilingPayment` | `PaymentDeclined` | `ReleasingReservation` (sem reserva: `Cancelled`) | `InventoryReleaseRequested` |
| `ReleasingReservation` | `InventoryReleased` | `Cancelled` | — |
| `StartingExecution` / `InExecution` | `ExecutionStartRejected` / `ExecutionFailed` | `Compensating` | `PaymentRefundRequested` e `InventoryReleaseRequested` |
| `InExecution` | cancelamento pela API | `CancellingExecution` | `ExecutionCancelRequested` |
| `CancellingExecution` | `ExecutionCancelled` / `ExecutionCancelRejected` | `Compensating` / `InExecution` | estorno e liberação / — |
| `Compensating` | `PaymentRefunded` e `InventoryReleased` | `Compensated` quando os dois chegam | — |
| `Compensating` | `PaymentRefundPending` / `PaymentRefundFailed` | `Compensating` / `ManualActionRequired` | — |

- **Cancelamento com participante respondendo** (`ReservingInventory` até `StartingExecution`): o pedido fica registrado e a resposta decide. Reserva confirmada vira liberação; pagamento aprovado vira estorno e liberação; execução iniciada vira `ExecutionCancelRequested`.
- **Efeito depois do encerramento:** `InventoryReserved` ou `PaymentApproved` que chega com a Saga já cancelada pede liberação ou estorno, para não prender estoque nem dinheiro.
- **Prazos:** etapas que esperam participante (`RequestingQuote`, `ReservingInventory`, `CreatingPayment`, `StartingExecution`, `ReleasingReservation`, `Compensating`, `CancellingExecution`) reenviam os comandos com a mesma `idempotencyKey` a cada prazo vencido. Esgotados os reenvios, a Saga vai para `ManualActionRequired`.
- **Ação manual:** prazo esgotado, pagamento com valor ou moeda divergentes e estorno recusado. O Admin escolhe `Cancelar` (compensa o que foi confirmado) ou `Retomar` (volta à etapa e reenvia). Pagamento divergente só aceita `Cancelar`.
- **Concorrência:** `xmin` da Saga como token. Duas mensagens da mesma Saga ao mesmo tempo: uma grava, a outra é refeita com o estado novo.
- **Métricas:** `os_saga_mensagens` (tipo e resultado: `Aplicada`, `ForaDoFluxo`, `Duplicada`, `Rejeitada`) e `os_saga_acao_manual` (causa), em `/metrics`.

### Recebimento e publicação

- **Duplicata:** o mesmo `messageId` é confirmado sem novo efeito.
- **Fora do contrato ou sem Saga:** tipo, versão, produtor ou payload incompatível, ou OS sem Saga, vai para a DLQ com o cabeçalho `x-motivo`. A transação é desfeita, então nem a inbox fica.
- **Falha transitória** (incluindo conflito de concorrência): 3 novas tentativas (1 s, 5 s, 10 s) antes da DLQ.
- **Outbox:** o despachante publica os comandos pendentes na exchange do canal, com confirmação do broker, e marca. A entrega é ao menos uma vez; o participante deduplica.

O `correlationId` (Id da Saga) vai para o escopo de log e para o trace, que continua o `traceparent` recebido e segue no comando publicado.

## Testes

```sh
dotnet test tests/OficinaMecanica.OS.UnitTests          # domínio, casos de uso e contratos AsyncAPI
dotnet test tests/OficinaMecanica.OS.IntegrationTests   # PostgreSQL e RabbitMQ via Testcontainers (Docker)
```

Os testes de contrato leem `contratos/asyncapi-saga-os.yaml`:

- Os catálogos de canais consumidos e publicados do código devem ser iguais aos da spec.
- Cada comando montado a partir da Saga deve passar no JSON Schema da spec.
- Cada exemplo e cada mutação inválida devem receber o mesmo veredito da spec e do validador do OS.

Ao mudar a spec em `tech-challenge-docs`, copie o arquivo para cá no mesmo ciclo.

## BDD ponta a ponta

`e2e/` sobe OS, Billing (modo `Simulado`) e Operações com um RabbitMQ só, um PostgreSQL por serviço, DynamoDB Local e Jaeger, e roda os cenários de `e2e/OficinaMecanica.E2E/Features/SagaOrdemServico.feature` (Reqnroll, Gherkin em português). Os steps usam só as APIs públicas, com o papel de quem faria cada ação (CARD-40c).

```sh
# os três repositórios lado a lado: tech-challenge-os, tech-challenge-billing, tech-challenge-operacoes
./e2e/executar.sh                      # sobe, testa e derruba; relatório em e2e/resultados/bdd.trx
MANTER_AMBIENTE=1 ./e2e/executar.sh    # deixa os containers no ar (Jaeger em http://localhost:16690)
```

| Cenário | O que prova |
|---|---|
| Concluir fluxo de OS com pagamento aprovado (obrigatório) | Abertura → diagnóstico → orçamento aprovado → reserva → Pix aprovado → execução → `Finalizada`, com histórico, pagamento aprovado e baixa de estoque |
| Rejeitar orçamento | `Cancelled` sem cobrança e sem execução |
| Falta de estoque na filial | `Cancelled` sem cobrança e sem mexer no estoque |
| Pagamento recusado | Reserva liberada e `Cancelled` |
| Falha operacional depois do pagamento | Estorno e liberação, `Compensated` |

O workflow `.github/workflows/e2e.yml` faz checkout dos três repositórios e roda o mesmo script em cada push e pull request.
