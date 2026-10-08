# tech-challenge-os

Serviço **OS** da Fase 4 do Tech Challenge (oficina mecânica). É dono de clientes, veículos, filiais, usuários funcionários e da ordem de serviço, com status e histórico. Também hospedará a Saga orquestrada da OS (CARD-40).

Decisões e contratos ficam em [tech-challenge-docs](https://github.com/LucazDenadai/tech-challenge-docs): limites e ownership ([ADR-014](https://github.com/LucazDenadai/tech-challenge-docs/blob/main/adr/ADR-014-limites-microsservicos-fase4.md), [ADR-015](https://github.com/LucazDenadai/tech-challenge-docs/blob/main/adr/ADR-015-ownership-e-infraestrutura-fase4.md)), banco ([ADR-016](https://github.com/LucazDenadai/tech-challenge-docs/blob/main/adr/ADR-016-bancos-sql-nosql-fase4.md)), Saga ([ADR-017](https://github.com/LucazDenadai/tech-challenge-docs/blob/main/adr/ADR-017-saga-orquestrada-os-fase4.md)) e contratos assíncronos ([ADR-018](https://github.com/LucazDenadai/tech-challenge-docs/blob/main/adr/ADR-018-contratos-assincronos-asyncapi-fase4.md)).

## Estrutura

```
src/OficinaMecanica.OS.Domain          entidades e regras de transição da OS
src/OficinaMecanica.OS.Application     casos de uso, portas e DTOs dos contratos da Saga
src/OficinaMecanica.OS.Infrastructure  EF Core/PostgreSQL, inbox, consumidor RabbitMQ, JWT, e-mail
src/OficinaMecanica.OS.API             controllers, autenticação, Swagger, health checks
tests/                                 testes unitários, de contrato e de integração
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
| `RabbitMq__Enabled`, `__Host`, `__Port`, `__VirtualHost`, `__Username`, `__Password` | Consumidor da Saga. Habilitado, exige usuário e senha. |
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
| `/os/clientes`, `/os/veiculos` | Admin, Atendente (leitura de veículo também Mecânico) | Cadastros |
| `/os/usuarios` | Admin | Usuários funcionários |
| `GET /os/filiais` | funcionários | Filiais ativas, para o `filialId` da abertura |
| `POST /os/ordens-servico` | Admin, Atendente | Abre a OS em `EmDiagnostico` |
| `GET /os/ordens-servico[/{id}[/status\|/historico]]` | funcionários | Lista, detalhe, status e histórico |
| `POST /os/ordens-servico/{id}/entrega`, `/cancelamento` | Admin, Atendente | Únicas transições manuais. O resto do status muda pela Saga (ADR-017). |
| `GET /os/ordens-servico/acompanhar/{numero}` | Cliente (JWT da Lambda) | Acompanhamento só das próprias OS |
| `POST /os/interno/clientes/busca-por-cpf` | `X-Api-Key` | Devolve `clienteId` e `ativo` para a Lambda. Não deve ser exposto no API Gateway. |

Todas as respostas devolvem o cabeçalho `X-Correlation-Id`, recebido ou gerado.

## Mensageria

O serviço consome os 21 canais cujo consumidor é o OS no AsyncAPI. Para cada canal `saga-os.<mensagem>.v1` (exchange fanout), declara a fila `os.<canal>` e a DLQ `os.<canal>.dlq`.

Cada mensagem é validada contra o contrato do canal e gravada na tabela `InboxMensagens`, deduplicada por `messageId`:

- **Duplicata:** a mensagem é confirmada sem gerar novo registro.
- **Fora do contrato:** tipo, versão, produtor ou payload incompatível vai para a DLQ com o cabeçalho `x-motivo`.
- **Falha transitória:** há 3 novas tentativas (1 s, 5 s, 10 s) antes da DLQ.

O `correlationId` vai para o escopo de log e para o trace, que continua o `traceparent` recebido. O efeito de negócio de cada evento (transição da Saga e do status) é implementado no CARD-40.

## Testes

```sh
dotnet test tests/OficinaMecanica.OS.UnitTests          # domínio, casos de uso e contratos AsyncAPI
dotnet test tests/OficinaMecanica.OS.IntegrationTests   # PostgreSQL e RabbitMQ via Testcontainers (Docker)
```

Os testes de contrato leem `contratos/asyncapi-saga-os.yaml`:

- O catálogo de canais do código deve ser igual ao da spec.
- Cada exemplo e cada mutação inválida devem receber o mesmo veredito da spec e do validador do OS.

Ao mudar a spec em `tech-challenge-docs`, copie o arquivo para cá no mesmo ciclo.
