#!/usr/bin/env sh
# BDD ponta a ponta (CARD-40c): sobe OS, Billing e Operações com um RabbitMQ só, roda os cenários Gherkin e
# derruba o ambiente. Os três repositórios precisam estar lado a lado (ver docker-compose.yml).
#
#   ./e2e/executar.sh            sobe, testa e derruba
#   MANTER_AMBIENTE=1 ./e2e/executar.sh   deixa os containers no ar para investigar
#
# Os testes rodam em um container do SDK dentro da rede do Compose: chamam os serviços pelo nome (os, billing,
# operacoes) e não dependem do .NET do host. O relatório TRX fica em e2e/resultados.
set -eu

cd "$(dirname "$0")"
PROJETO=tc-e2e
# Git Bash no Windows: o Docker precisa do caminho no formato D:/...
RAIZ=$( (cd .. && pwd -W 2>/dev/null) || (cd .. && pwd) )
export MSYS_NO_PATHCONV=1

derrubar() {
    if [ "${MANTER_AMBIENTE:-0}" != "1" ]; then
        docker compose -p "$PROJETO" down --remove-orphans >/dev/null 2>&1 || true
    fi
}
trap derrubar EXIT

docker compose -p "$PROJETO" up -d --build

rm -rf resultados && mkdir -p resultados
docker run --rm --network "${PROJETO}_default" \
    -v "$RAIZ:/origem:ro" \
    -v "$RAIZ/e2e/resultados:/resultados" \
    -v nuget-cache:/root/.nuget/packages \
    -e E2E_OS_URL=http://os:8080 \
    -e E2E_BILLING_URL=http://billing:8080 \
    -e E2E_OPERACOES_URL=http://operacoes:8080 \
    mcr.microsoft.com/dotnet/sdk:10.0 sh -c '
        set -e
        for servico in os:os billing:billing operacoes:operacoes; do
            host=${servico%%:*}; rota=${servico##*:}
            tentativas=0
            until curl -sf "http://$host:8080/$rota/ready" >/dev/null; do
                tentativas=$((tentativas + 1))
                [ "$tentativas" -gt 90 ] && echo "$host não ficou pronto" && exit 1
                sleep 2
            done
            echo "$host pronto"
        done
        mkdir -p /e2e && cp -r /origem/e2e/OficinaMecanica.E2E /e2e/ && cd /e2e/OficinaMecanica.E2E
        rm -rf bin obj
        dotnet test --nologo --logger "trx;LogFileName=bdd.trx" --logger "console;verbosity=normal" --results-directory /resultados
    '
