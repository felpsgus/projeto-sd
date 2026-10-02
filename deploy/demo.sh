#!/usr/bin/env bash
# Parte de linha de comando do roteiro da apresentação do T2, em atos,
# avançando a cada Enter.
#
#     DEMO_PASSWORD=... ./demo.sh              # ensaio/apresentação
#     DEMO_PASSWORD=... ./demo.sh --warmup     # só a chamada de aquecimento, sem exibir nada
#     DEMO_PASSWORD=... ./demo.sh --falha      # inclui o Ato opcional de indisponibilidade (503)
#
# Desde BE-42, o roteiro de verdade (deploy/README.md, "No dia da
# apresentação") PARTE DO FRONTEND, no navegador: login, criar tarefa com
# título vazio (400 no formulário) e criar tarefa válida (201, aparece na
# lista) acontecem ali, não aqui. Este script cobre o que o navegador não
# mostra bem sob pressão de tempo — o 401 em três variações e o caminho de
# indisponibilidade — e serve de ensaio/backup em linha de comando para os
# mesmos atos, e de apoio ao "mostrar o código" (o handler que recebe cada
# chamada). Por isso ele ainda fala HTTP puro, e não abre navegador nenhum.
#
# Por que um script e não digitar na hora: ninguém digita e-mail/senha/token
# sob pressão sem errar, e o limite de 10 minutos (t2.md) não perdoa. Cada
# ato para e espera Enter — você narra, o comando aparece na tela, você
# aperta Enter e a resposta aparece.
#
# BASE aponta para o nginx (porta 80), não mais direto no Gateway (:8080) —
# desde BE-42 o Gateway só escuta em 127.0.0.1:8080 e o nginx é quem
# responde na porta pública; falar com ele aqui, mesmo estando os dois na
# mesma VM, é o que de fato prova o caminho que a plateia vai ver.
# Onda E (T2): o seed de demonstração (DemoUserSeeder) foi removido — não há
# mais um usuário fixo pronto para logar. Este script cadastra a conta que
# usa (POST /api/auth/register) num Ato próprio, com e-mail gerado a partir
# do relógio; se você já cadastrou uma conta pela tela minutos antes (o
# roteiro de verdade parte do frontend), pode reaproveitá-la passando
# DEMO_EMAIL — sem isso, o script cadastra a sua própria.
set -uo pipefail

BASE=http://127.0.0.1
EMAIL_ATIVO="${DEMO_EMAIL:-demo-t2-$(date +%Y%m%d%H%M%S%N)@todolist.example}"
CADASTRAR_CONTA=1
[[ -n "${DEMO_EMAIL:-}" ]] && CADASTRAR_CONTA=0

if [[ -z "${DEMO_PASSWORD:-}" ]]; then
    echo "Defina DEMO_PASSWORD antes de rodar:" >&2
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

# Cadastro real (POST /api/auth/register) — substitui o antigo usuário fixo
# do seed (removido, Onda E). Silencioso de propósito: o cadastro "visível"
# do roteiro acontece no navegador (comentário no topo do arquivo); aqui só
# precisamos que a conta EXISTA antes do Ato 3 (login) rodar. Idempotente na
# prática porque EMAIL_ATIVO é novo a cada execução quando DEMO_EMAIL não é
# informado — um 409 (e-mail já cadastrado) só acontece se você reaproveitar
# manualmente um DEMO_EMAIL já usado, e nesse caso o login do Ato 3 funciona
# do mesmo jeito (a conta já existe).
cadastrar() {
    [[ $CADASTRAR_CONTA -eq 1 ]] || return 0
    curl -s -o /dev/null --max-time 20 -X POST "$BASE/api/auth/register" \
        -H 'Content-Type: application/json' \
        -d "{\"email\":\"$EMAIL_ATIVO\",\"password\":\"$DEMO_PASSWORD\",\"displayName\":\"Demo T2\"}" || true
}

# Aquecimento: a primeira chamada de um processo recém-iniciado paga o
# estabelecimento da conexão HTTP/2 com Identity/Tasks e a primeira query do
# EF Core. Que esse custo aconteça aqui, e não na primeira requisição diante
# da banca.
aquecer() {
    cadastrar
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

# Compose da VM (onde install-docker-on-vm.sh o instala) e nome real do
# serviço no docker-compose.prod.yml. sudo porque quem roda a demo
# normalmente não está no grupo docker.
COMPOSE_DIR=/opt/todolist/docker
dc() {
    sudo docker compose -f "$COMPOSE_DIR/docker-compose.prod.yml" --env-file "$COMPOSE_DIR/.env" "$@"
}

# Se o roteiro for interrompido no meio do ato de indisponibilidade, o
# Identity ficaria parado — e o próximo smoke.sh daria 503 sem motivo
# aparente. O trap garante que ele volte, aconteça o que acontecer. Só age se
# este script chegou a parar o Identity (flag), para não chamar o Docker à toa.
identity_parado=0
restaurar_identity() {
    [[ $identity_parado -eq 1 ]] || return 0
    if ! dc ps --status running --services 2>/dev/null | grep -qx identity; then
        echo ""
        echo "${AMARELO}Religando o Identity...${FIM}"
        dc start identity
        sleep 2
    fi
    identity_parado=0
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
echo "${CINZA}Lembrete: o roteiro de verdade parte do frontend, no navegador (login, título vazio,${FIM}"
echo "${CINZA}tarefa válida). Os atos abaixo são o apoio em linha de comando — 401 e indisponibilidade —${FIM}"
echo "${CINZA}que o deploy/smoke.sh, rodado num SEGUNDO terminal (notebook, Git Bash), também cobre.${FIM}"
echo "${CINZA}Conta desta execução: $EMAIL_ATIVO${FIM}"
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
    identity_parado=1
    dc stop identity
    echo "${CINZA}Identity parado. Mesma requisição do Ato 5:${FIM}"
    echo ""
    criar_tarefa "$token" "Identity fora do ar"
    echo ""
    dc start identity
    sleep 2
    identity_parado=0
    echo "${VERDE}Identity religado.${FIM}"
fi

echo ""
echo "${VERDE}Fim. Nos três painéis de log (Gateway, Tasks, Identity) procure o mesmo traceId do Ato 5:${FIM}"
echo "${CINZA}  sudo docker compose -f $COMPOSE_DIR/docker-compose.prod.yml --env-file $COMPOSE_DIR/.env \\${FIM}"
echo "${CINZA}    logs --since 2m gateway tasks identity | grep -E 'ValidateToken|CreateTask|ValidateUser'${FIM}"
echo ""
echo "${CINZA}O quarto processo em jogo é o nginx, que roda no container frontend —${FIM}"
echo "${CINZA}ele só encaminha bytes (D-40): o traceparent atravessa intacto, então não é esperado${FIM}"
echo "${CINZA}vê-lo como uma quarta linha correlacionada. Se fizer sentido narrar o roteamento em si:${FIM}"
echo "${CINZA}  sudo docker compose -f $COMPOSE_DIR/docker-compose.prod.yml --env-file $COMPOSE_DIR/.env logs -f frontend${FIM}"
echo ""
