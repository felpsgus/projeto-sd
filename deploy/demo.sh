#!/usr/bin/env bash
# Roteiro da apresentação, em três atos, avançando a cada Enter.
#
#     ./demo.sh              # ensaio/apresentação
#     ./demo.sh --warmup     # só a chamada de aquecimento, sem exibir nada
#
# Por que um script e não digitar na hora: são 5 minutos, e ninguém digita um
# GUID de 36 caracteres sob pressão. Cada ato para e espera Enter — você narra, o
# comando aparece na tela, você aperta Enter e a resposta aparece.
set -uo pipefail

BASE=http://127.0.0.1:5100
DONO_ATIVO=10000000-0000-0000-0000-000000000001
DONO_INEXISTENTE=99999999-9999-9999-9999-999999999999

VERDE=$'\033[32m'; AMARELO=$'\033[33m'; CIANO=$'\033[36m'; CINZA=$'\033[90m'; FIM=$'\033[0m'

criar_tarefa() {
    local user_id="$1" titulo="$2"
    curl -s -i -X POST "$BASE/api/tasks" \
        -H 'Content-Type: application/json' \
        -H "X-User-Id: $user_id" \
        -d "{\"title\":\"$titulo\"}" \
    | sed -n '1p;/^{/p'
}

# Aquecimento: a primeira chamada de um processo recém-iniciado paga o
# estabelecimento da conexão HTTP/2 e a primeira query do EF Core. Que esse custo
# aconteça aqui, e não na primeira requisição diante da banca.
aquecer() {
    curl -s -o /dev/null --max-time 20 -X POST "$BASE/api/tasks" \
        -H 'Content-Type: application/json' \
        -H "X-User-Id: $DONO_ATIVO" \
        -d '{"title":"aquecimento (descartavel)"}' || true
}

if [[ "${1:-}" == "--warmup" ]]; then
    echo "Aquecendo..."
    aquecer
    echo "Pronto. A primeira chamada da apresentação já encontra tudo quente."
    exit 0
fi

# Se o roteiro for interrompido no meio do ato 3, o Identity ficaria parado — e o
# próximo teste daria 503 sem motivo aparente. O trap garante que ele volte,
# aconteça o que acontecer.
restaurar_identity() {
    if ! systemctl is-active --quiet todolist-identity; then
        echo ""
        echo "${AMARELO}Religando o Identity...${FIM}"
        sudo systemctl start todolist-identity
        sleep 2
    fi
}
trap restaurar_identity EXIT

ato() {
    echo ""
    echo "${CIANO}════════════════════════════════════════════════════════════${FIM}"
    echo "${CIANO} $1${FIM}"
    echo "${CIANO}════════════════════════════════════════════════════════════${FIM}"
    echo ""
    read -rsp "$(printf '%s' "${CINZA}[Enter]${FIM}")"
    echo ""
}

clear
echo "${VERDE}TodoList — comunicação gRPC entre Tasks e Identity${FIM}"
echo "${CINZA}Aquecendo antes de começar...${FIM}"
aquecer

ato "ATO 1 — dono válido: a tarefa é criada"
echo "${CINZA}POST /api/tasks   X-User-Id: $DONO_ATIVO${FIM}"
echo ""
criar_tarefa "$DONO_ATIVO" "Preparar a demonstracao do T1"
echo ""
echo "${CINZA}Nos dois painéis de log: a mesma chamada ValidateUser, com o mesmo traceId.${FIM}"

ato "ATO 2 — a MESMA requisição, só mudando o dono: o Identity nega"
echo "${CINZA}POST /api/tasks   X-User-Id: $DONO_INEXISTENTE${FIM}"
echo ""
criar_tarefa "$DONO_INEXISTENTE" "Dono que nao existe"
echo ""
echo "${CINZA}O Identity respondeu exists=False. A decisão não é do Tasks.${FIM}"

ato "ATO 3 — Identity fora do ar: fail-closed, nada é gravado"
sudo systemctl stop todolist-identity
echo "${CINZA}Identity parado. Mesma requisição do Ato 1:${FIM}"
echo ""
criar_tarefa "$DONO_ATIVO" "Identity fora do ar"
echo ""
sudo systemctl start todolist-identity
echo "${VERDE}Identity religado.${FIM}"

echo ""
echo "${VERDE}Fim.${FIM}"
echo ""
