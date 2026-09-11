#!/usr/bin/env bash
# Roteiro da apresentação do T2, em atos, avançando a cada Enter.
#
#     DEMO_PASSWORD=... ./demo.sh              # ensaio/apresentação
#     DEMO_PASSWORD=... ./demo.sh --warmup     # só a chamada de aquecimento, sem exibir nada
#     DEMO_PASSWORD=... ./demo.sh --falha      # inclui o Ato opcional de indisponibilidade (503)
#
# Por que um script e não digitar na hora: são 5 minutos (t2.md) e ninguém
# digita e-mail/senha/token sob pressão sem errar. Cada ato para e espera
# Enter — você narra, o comando aparece na tela, você aperta Enter e a
# resposta aparece.
set -uo pipefail

BASE=http://127.0.0.1:8080
EMAIL_ATIVO='ada.lovelace@todolist.example'

if [[ -z "${DEMO_PASSWORD:-}" ]]; then
    echo "Defina DEMO_PASSWORD (UserStore:DemoUserPassword) antes de rodar:" >&2
    echo "  DEMO_PASSWORD=sua-senha-de-demo ./demo.sh" >&2
    exit 1
fi

VERDE=$'\033[32m'; AMARELO=$'\033[33m'; CIANO=$'\033[36m'; CINZA=$'\033[90m'; FIM=$'\033[0m'

CORPO="$(mktemp)"
trap 'rm -f "$CORPO"' EXIT

extrair_token() {
    sed -n 's/.*"accessToken":"\([^"]*\)".*/\1/p' "$CORPO"
}

login() {
    curl -s -i -X POST "$BASE/api/auth/login" \
        -H 'Content-Type: application/json' \
        -d "{\"email\":\"$EMAIL_ATIVO\",\"password\":\"$DEMO_PASSWORD\"}" \
        -o "$CORPO" -D - \
    | sed -n '1p'
    cat "$CORPO"
}

criar_tarefa() {
    local token="$1" titulo="$2"
    local args=(-s -i -X POST "$BASE/api/tasks" -H 'Content-Type: application/json')
    # Token vazio == Ato 1 (sem Authorization nenhum) — token arbitrário
    # (lixo ou válido) vai no header normalmente.
    [[ -n "$token" ]] && args+=(-H "Authorization: Bearer $token")
    args+=(-d "{\"title\":\"$titulo\"}")

    curl "${args[@]}" | sed -n '1p;/^{/p'
}

# Aquecimento: a primeira chamada de um processo recém-iniciado paga o
# estabelecimento da conexão HTTP/2 com Identity/Tasks e a primeira query do
# EF Core. Que esse custo aconteça aqui, e não na primeira requisição diante
# da banca.
aquecer() {
    curl -s -o /dev/null --max-time 20 -X POST "$BASE/api/auth/login" \
        -H 'Content-Type: application/json' \
        -d "{\"email\":\"$EMAIL_ATIVO\",\"password\":\"$DEMO_PASSWORD\"}" || true
}

if [[ "${1:-}" == "--warmup" ]]; then
    echo "Aquecendo..."
    aquecer
    echo "Pronto. A primeira chamada da apresentação já encontra tudo quente."
    exit 0
fi

incluir_falha=0
[[ "${1:-}" == "--falha" ]] && incluir_falha=1

# Se o roteiro for interrompido no meio do ato de indisponibilidade, o
# Identity ficaria parado — e o próximo smoke.sh daria 503 sem motivo
# aparente. O trap garante que ele volte, aconteça o que acontecer.
restaurar_identity() {
    if ! systemctl is-active --quiet todolist-identity; then
        echo ""
        echo "${AMARELO}Religando o Identity...${FIM}"
        sudo systemctl start todolist-identity
        sleep 2
    fi
}
trap 'restaurar_identity; rm -f "$CORPO"' EXIT

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
echo "${VERDE}TodoList — API Gateway: REST na borda, gRPC por dentro (T2)${FIM}"
echo "${CINZA}Aquecendo antes de começar...${FIM}"
aquecer

ato "ATO 1 — sem token: o Gateway recusa antes de falar com qualquer backend"
echo "${CINZA}POST /api/tasks   (sem Authorization)${FIM}"
echo ""
criar_tarefa "" "Tarefa sem autenticacao"
echo ""
echo "${CINZA}401 auth.unauthorized — o middleware de autenticação barra na borda.${FIM}"

ato "ATO 2 — token lixo: mesmo 401, caminho de código diferente"
echo "${CINZA}POST /api/tasks   Authorization: Bearer token-lixo-arbitrario${FIM}"
echo ""
criar_tarefa "token-lixo-arbitrario" "Tarefa com token invalido"
echo ""
echo "${CINZA}Corpo idêntico ao do Ato 1 — o cliente nunca sabe qual dos dois motivos foi.${FIM}"

ato "ATO 3 — login: e-mail/senha viram um access token"
echo "${CINZA}POST /api/auth/login   $EMAIL_ATIVO${FIM}"
echo ""
login
echo ""
token="$(extrair_token)"
# Nunca o token inteiro na tela projetada (RN-AUTH-05) — só o suficiente para
# reconhecer que ele existe.
echo "${CINZA}accessToken: ${token:0:12}...(truncado)${FIM}"

ato "ATO 4 — título vazio: validação na borda, 400, nenhuma chamada ao Tasks"
echo "${CINZA}POST /api/tasks   título=\"\"${FIM}"
echo ""
criar_tarefa "$token" ""
echo ""
echo "${CINZA}400 — rejeitado antes de qualquer tradução para gRPC.${FIM}"

ato "ATO 5 — caminho de sucesso: 201, tradução JSON -> gRPC completa"
echo "${CINZA}POST /api/tasks   título válido${FIM}"
echo ""
resposta_final="$(criar_tarefa "$token" "Demonstracao do T2 - API Gateway")"
echo "$resposta_final"
echo ""
echo "${CINZA}Location aponta para o recurso criado; a tarefa já está no banco do Tasks.${FIM}"
echo "${AMARELO}Anote o traceId dos logs (próximo passo) para achar as três linhas correlacionadas.${FIM}"

if [[ $incluir_falha -eq 1 ]]; then
    ato "ATO OPCIONAL — Identity fora do ar: 503 com Retry-After, nunca 401"
    sudo systemctl stop todolist-identity
    echo "${CINZA}Identity parado. Mesma requisição do Ato 5:${FIM}"
    echo ""
    criar_tarefa "$token" "Identity fora do ar"
    echo ""
    sudo systemctl start todolist-identity
    sleep 2
    echo "${VERDE}Identity religado.${FIM}"
fi

echo ""
echo "${VERDE}Fim. Nos três painéis de log (Gateway, Tasks, Identity) procure o mesmo traceId do Ato 5:${FIM}"
echo "${CINZA}  sudo journalctl -u todolist-gateway -u todolist-tasks -u todolist-identity --since '2 min ago' \\${FIM}"
echo "${CINZA}    | grep -E 'ValidateToken|CreateTask|ValidateUser'${FIM}"
echo ""
