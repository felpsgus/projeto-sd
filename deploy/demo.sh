#!/usr/bin/env bash
# Parte de linha de comando do roteiro da apresentação do T2, em atos,
# avançando a cada Enter.
#
#     DEMO_PASSWORD=... ./demo.sh              # ensaio/apresentação
#     DEMO_PASSWORD=... ./demo.sh --warmup     # só a chamada de aquecimento, sem exibir nada
#     DEMO_PASSWORD=... ./demo.sh --falha      # inclui o Ato opcional de indisponibilidade (503)
#
# O roteiro (deploy/README.md, "No dia da apresentação") PARTE DO FRONTEND, no
# navegador: cadastro, login e criar tarefa válida (201, aparece na lista)
# acontecem ali. Este script cobre o que a tela não mostra: o 401, o 400 de
# título vazio (o formulário do Angular valida antes de enviar, então pela
# tela a requisição nunca chega ao Gateway), o 201 por HTTP puro, os logs
# correlacionados e a linha no banco. Serve também de apoio ao "mostrar o
# código" (o handler que recebe cada chamada).
#
# O script também mostra, no próprio terminal, os logs correlacionados da
# chamada de sucesso (Ato 6) e a linha gravada no banco (Ato 7): não precisa de
# painel de log nem de segundo terminal. O nginx não aparece na correlação —
# ele só repassa o traceparent (D-40).
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
FALHAS="$(mktemp)"
trap 'rm -f "$CORPO" "$FALHAS"' EXIT

extrair_token() {
    sed -n 's/.*"accessToken":"\([^"]*\)".*/\1/p' "$CORPO"
}

login() {
    curl -s -i -X POST "$BASE/api/auth/login" \
        -H 'Content-Type: application/json' \
        -d "{\"email\":\"$EMAIL_ATIVO\",\"password\":\"$DEMO_PASSWORD\"}" \
        -o "$CORPO" -D - \
    | sed -n '1p'
    # O corpo vai para a tela projetada: o access token sai truncado (RN-AUTH-05).
    sed 's/"accessToken":"\([^"]\{12\}\)[^"]*"/"accessToken":"\1...(truncado)"/' "$CORPO"
}

criar_tarefa() {
    local token="$1" titulo="$2" esperado="$3" traceparent="${4:-}"
    local args=(-s -i -X POST "$BASE/api/tasks" -H 'Content-Type: application/json')
    # Token vazio == Ato 1 (sem Authorization nenhum) — token arbitrário
    # (lixo ou válido) vai no header normalmente.
    [[ -n "$token" ]] && args+=(-H "Authorization: Bearer $token")
    [[ -n "$traceparent" ]] && args+=(-H "traceparent: $traceparent")
    args+=(-d "{\"title\":\"$titulo\"}")

    local resposta status
    resposta="$(curl "${args[@]}")"
    sed -n '1p;/^{/p' <<<"$resposta"
    # Status diferente do esperado do ato -> o script sai com 1 no fim. Arquivo
    # (e não variável) porque o Ato 5 chama esta função dentro de $(...).
    status="$(sed -n '1s/^HTTP[^ ]* \([0-9]*\).*/\1/p' <<<"$resposta")"
    [[ "$status" == "$esperado" ]] || echo "esperado $esperado, veio ${status:-nada}" >>"$FALHAS"
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
trap 'restaurar_identity; rm -f "$CORPO" "$FALHAS"' EXIT

# Hex aleatório de $1 bytes (openssl, ou o UUID do kernel sem hífens).
hex() {
    openssl rand -hex "$1" 2>/dev/null \
        || tr -d - </proc/sys/kernel/random/uuid | cut -c1-$(($1 * 2))
}

# Lê do stdin as linhas cruas do `compose logs` (prefixo + JSON do Serilog) e
# imprime "HH:MM:SS.mmm  serviço   mensagem", ordenado pelo @t. Sem jq: o awk
# lê o @m caractere a caractere para tratar o \" escapado.
formatar_logs() {
    awk -v c_gw="$CIANO" -v c_ta="$VERDE" -v c_id="$AMARELO" -v fim="$FIM" '
    {
        svc = $1; sub(/-[0-9]+$/, "", svc)
        i = index($0, "\"@t\":\""); j = index($0, "\"@m\":\"")
        if (!i || !j) next
        t = substr($0, i + 6); t = substr(t, 1, index(t, "\"") - 1)
        r = substr($0, j + 6); m = ""
        for (k = 1; k <= length(r); k++) {
            ch = substr(r, k, 1)
            if (ch == "\\") { k++; m = m substr(r, k, 1) }
            else if (ch == "\"") break
            else m = m ch
        }
        cor = (svc == "gateway") ? c_gw : (svc == "tasks") ? c_ta : c_id
        printf "%s\t%s  %s%-8s%s  %s\n", t, substr(t, 12, 12), cor, svc, fim, m
    }' | sort -s -t $'\t' -k1,1 | cut -f2-
}

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
echo "${CINZA}Lembrete: cadastro, login e tarefa válida acontecem antes, no navegador. Aqui saem o 401,${FIM}"
echo "${CINZA}o 400 de título vazio, o 201, os logs correlacionados e a linha no banco.${FIM}"
echo "${CINZA}Conta desta execução: $EMAIL_ATIVO${FIM}"
echo "${CINZA}Aquecendo antes de começar...${FIM}"
aquecer

ato "ATO 1 — sem token: o Gateway recusa antes de falar com qualquer backend"
echo "${CINZA}POST /api/tasks   (sem Authorization)${FIM}"
echo ""
criar_tarefa "" "Tarefa sem autenticacao" 401
echo ""
echo "${CINZA}401 auth.unauthorized — o middleware de autenticação barra na borda.${FIM}"

ato "ATO 2 — token lixo: mesmo 401, caminho de código diferente"
echo "${CINZA}POST /api/tasks   Authorization: Bearer token-lixo-arbitrario${FIM}"
echo ""
criar_tarefa "token-lixo-arbitrario" "Tarefa com token invalido" 401
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
criar_tarefa "$token" "" 400
echo ""
echo "${CINZA}400 — rejeitado antes de qualquer tradução para gRPC.${FIM}"

ato "ATO 5 — caminho de sucesso: 201, tradução JSON -> gRPC completa"
echo "${CINZA}POST /api/tasks   título válido${FIM}"
echo ""
trace_id="$(hex 16)"
echo "${CINZA}traceparent: 00-$trace_id-<span>-01${FIM}"
resposta_final="$(criar_tarefa "$token" "Demonstracao do T2 - API Gateway" 201 "00-$trace_id-$(hex 8)-01")"
echo "$resposta_final"
tarefa_id="$(sed -n 's/.*"id":"\([0-9a-fA-F-]*\)".*/\1/p' <<<"$resposta_final")"
echo ""
echo "${CINZA}Location aponta para o recurso criado; a tarefa já está no banco do Tasks.${FIM}"
echo "${CINZA}O traceId acima atravessa Gateway, Tasks e Identity: é o que o próximo ato filtra.${FIM}"

ato "ATO 6 — logs correlacionados: o mesmo traceId nos três serviços"
echo "${CINZA}docker compose logs gateway tasks identity | filtra traceId $trace_id${FIM}"
echo ""
sleep 1
logs_trace="$(dc logs --no-color --since 5m gateway tasks identity 2>/dev/null | grep -F "$trace_id")"
if [[ -n "$logs_trace" ]]; then
    formatar_logs <<<"$logs_trace"
else
    echo "${AMARELO}Nenhuma linha de log com o traceId $trace_id.${FIM}"
    echo "Ato 6: nenhum log com o traceId $trace_id" >>"$FALHAS"
fi
echo ""
echo "${CINZA}O nginx não aparece: só repassa o traceparent, não loga o traceId (D-40).${FIM}"

ato "ATO 7 — a linha no banco: a tarefa do Ato 5 no Postgres real (Cloud SQL)"
if [[ "$tarefa_id" =~ ^[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}$ ]]; then
    sql="select t.id, t.title, u.email as dono, t.created_at from tasks.tasks t join identity.users u on u.id = t.owner_id where t.id = '$tarefa_id'"
    echo "${CINZA}$sql${FIM}"
    echo ""
    dc run --rm --entrypoint psql migrate -P pager=off -c "$sql" \
        || { echo "${AMARELO}psql falhou.${FIM}"; echo "Ato 7: psql falhou" >>"$FALHAS"; }
else
    echo "${AMARELO}Sem id de tarefa válido do Ato 5 (veio: '${tarefa_id}').${FIM}"
    echo "Ato 7: id da tarefa ausente ou inválido" >>"$FALHAS"
fi

if [[ $incluir_falha -eq 1 ]]; then
    ato "ATO OPCIONAL — Identity fora do ar: 503 com Retry-After, nunca 401"
    identity_parado=1
    dc stop identity
    echo "${CINZA}Identity parado. Mesma requisição do Ato 5:${FIM}"
    echo ""
    criar_tarefa "$token" "Identity fora do ar" 503
    echo ""
    dc start identity
    sleep 2
    identity_parado=0
    echo "${VERDE}Identity religado.${FIM}"
fi

echo ""
echo "${VERDE}Fim.${FIM}"
echo "${CINZA}O nginx (container frontend) só encaminha bytes (D-40): o traceparent atravessa intacto,${FIM}"
echo "${CINZA}então ele não aparece na correlação. Se fizer sentido narrar o roteamento em si:${FIM}"
echo "${CINZA}  sudo docker compose -f $COMPOSE_DIR/docker-compose.prod.yml --env-file $COMPOSE_DIR/.env logs -f frontend${FIM}"
echo ""
if [[ -s "$FALHAS" ]]; then
    echo "${AMARELO}Algum ato respondeu fora do esperado:${FIM}"
    cat "$FALHAS"
    exit 1
fi
