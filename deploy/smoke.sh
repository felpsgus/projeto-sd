#!/usr/bin/env bash
# Verificação de fumaça do T2: os seis passos de BE-39, contra o Gateway —
# a única borda REST agora (D-32). Equivalente, dentro da VM, do
# scripts/demo-t2.ps1 que roda do notebook Windows.
#
#     DEMO_PASSWORD=... ./smoke.sh                        # contra 127.0.0.1:8080
#     DEMO_PASSWORD=... ./smoke.sh http://10.128.0.4:8080
#
# Rode isto depois de todo deploy, e de novo cerca de uma hora antes da
# apresentação. DEMO_PASSWORD é o valor de UserStore:DemoUserPassword —
# nunca fixado neste script versionado; entra só por variável de ambiente.
set -uo pipefail

BASE="${1:-http://127.0.0.1:8080}"

if [[ -z "${DEMO_PASSWORD:-}" ]]; then
    echo "Defina DEMO_PASSWORD (UserStore:DemoUserPassword) antes de rodar:" >&2
    echo "  DEMO_PASSWORD=sua-senha-de-demo ./smoke.sh" >&2
    exit 1
fi

# Usuários do seed (DemoUserSeeder) — README, seção "Rodando o T2".
EMAIL_ATIVO='ada.lovelace@todolist.example'
EMAIL_INATIVO='charles.babbage@todolist.example'

CORPO="$(mktemp)"
CABECALHOS="$(mktemp)"
trap 'rm -f "$CORPO" "$CABECALHOS"' EXIT

falhas=0

# Uma linha OK/ERRO por passo — status HTTP e, quando informado, o errorCode
# no corpo. Sem -e (só -uo pipefail): um passo fora do esperado é um
# resultado a reportar, não um motivo para abortar e deixar os passos
# seguintes sem verificação nenhuma.
relatar() {
    local passo="$1" status="$2" esperado_status="$3" esperado_code="${4:-}"
    local corpo
    corpo="$(cat "$CORPO" 2>/dev/null || true)"

    local ok=1
    [[ "$status" == "$esperado_status" ]] || ok=0
    if [[ -n "$esperado_code" ]] && ! grep -q "\"$esperado_code\"" <<<"$corpo"; then ok=0; fi

    if [[ $ok -eq 1 ]]; then
        printf '  OK    %-56s HTTP %s %s\n' "$passo" "$status" "$esperado_code"
    else
        printf '  ERRO  %-56s HTTP %s (esperado %s %s)\n' "$passo" "$status" "$esperado_status" "$esperado_code"
        printf '        %s\n' "$corpo"
        falhas=$((falhas + 1))
    fi
}

# sed em vez de jq: jq nem sempre está instalado na VM, e o corpo é simples o
# bastante para não precisar de um parser JSON de verdade.
extrair_token() {
    sed -n 's/.*"accessToken":"\([^"]*\)".*/\1/p' "$CORPO"
}

echo "Gateway: $BASE"
echo ""

# 1. Sem token -> 401 (CA-05: os passos 1 e 2 são caminhos de código
# diferentes no middleware — ausência de header vs. header que falha).
status="$(curl -s -o "$CORPO" -w '%{http_code}' --max-time 15 \
    -X POST "$BASE/api/tasks" -H 'Content-Type: application/json' \
    -d '{"title":"smoke sem token"}')" || status=000
relatar '1. POST /api/tasks sem token' "$status" 401 auth.unauthorized

# 2. Token lixo -> 401 (mesmo errorCode do passo 1: CA-12, a causa não é
# distinguível pelo cliente).
status="$(curl -s -o "$CORPO" -w '%{http_code}' --max-time 15 \
    -X POST "$BASE/api/tasks" -H 'Content-Type: application/json' \
    -H 'Authorization: Bearer token-lixo-arbitrario' \
    -d '{"title":"smoke token lixo"}')" || status=000
relatar '2. POST /api/tasks com token lixo' "$status" 401 auth.unauthorized

# 3. Login do usuário ativo -> 200 com accessToken/expiresAt.
status="$(curl -s -o "$CORPO" -w '%{http_code}' --max-time 15 \
    -X POST "$BASE/api/auth/login" -H 'Content-Type: application/json' \
    -d "{\"email\":\"$EMAIL_ATIVO\",\"password\":\"$DEMO_PASSWORD\"}")" || status=000
relatar '3. POST /api/auth/login (usuário ativo)' "$status" 200

token="$(extrair_token)"
if [[ -z "$token" ]]; then
    echo "        Sem accessToken no corpo do passo 3 — os passos 4 e 5 vão falhar em cascata."
fi

# 4. Token válido, título vazio -> 400 (validação na borda, antes de qualquer
# chamada gRPC ao Tasks).
status="$(curl -s -o "$CORPO" -w '%{http_code}' --max-time 15 \
    -X POST "$BASE/api/tasks" -H 'Content-Type: application/json' \
    -H "Authorization: Bearer $token" \
    -d '{"title":""}')" || status=000
relatar '4. POST /api/tasks com título vazio' "$status" 400

# 5. Token válido, título válido -> 201 com Location. O status e o Location
# são conferidos em duas linhas separadas porque são duas evidências
# diferentes (BE-39 CA-01: "REST público" + "tradução JSON -> gRPC").
status="$(curl -s -o "$CORPO" -D "$CABECALHOS" -w '%{http_code}' --max-time 15 \
    -X POST "$BASE/api/tasks" -H 'Content-Type: application/json' \
    -H "Authorization: Bearer $token" \
    -d '{"title":"Verificacao smoke.sh do T2"}')" || status=000
relatar '5. POST /api/tasks válido' "$status" 201

if [[ "$status" == "201" ]] && grep -qi '^location:' "$CABECALHOS"; then
    local_location="$(grep -i '^location:' "$CABECALHOS" | tr -d '\r' | cut -d' ' -f2-)"
    printf '  OK    %-56s %s\n' '5b. Header Location presente' "$local_location"
else
    printf '  ERRO  %-56s (ausente)\n' '5b. Header Location presente'
    falhas=$((falhas + 1))
fi

# 6. Login do usuário inativo -> 401, corpo idêntico ao de senha errada
# (RN-AUTH-09: não revelar que o usuário existe, mas está inativo).
status="$(curl -s -o "$CORPO" -w '%{http_code}' --max-time 15 \
    -X POST "$BASE/api/auth/login" -H 'Content-Type: application/json' \
    -d "{\"email\":\"$EMAIL_INATIVO\",\"password\":\"$DEMO_PASSWORD\"}")" || status=000
relatar '6. POST /api/auth/login (usuário inativo)' "$status" 401 auth.invalid_credentials

echo ""
if [[ $falhas -eq 0 ]]; then
    echo "Tudo como esperado."
    echo ""
    echo "Evidência do traceId correlacionado (passo 5, os três serviços):"
    echo "  sudo journalctl -u todolist-gateway -u todolist-tasks -u todolist-identity --since '2 min ago' \\"
    echo "    | grep -E 'ValidateToken|CreateTask|ValidateUser'"
    exit 0
fi

echo "$falhas passo(s) fora do esperado."
echo "  sudo journalctl -u todolist-gateway -u todolist-tasks -u todolist-identity -n 150 --no-pager"
exit 1
