#!/usr/bin/env bash
# Verificação de fumaça do T3 (BE-45): rota do SPA + os passos HTTP de cadastro, login, 401, 400, 201 e listagem,
# contra a URL HTTPS do frontend no Cloud Run (ou http://localhost no compose de dev).
#
#     DEMO_PASSWORD=... bash deploy/cloudrun/smoke.sh https://todolist-frontend-xxxx-uc.a.run.app
#
# Rode depois de todo deploy e de novo ~30 min antes da apresentação (depois do Warmup).
# DEMO_PASSWORD é a senha da conta que o próprio script cadastra; nunca versionada, só por variável de ambiente.
# Cadastra uma conta nova a cada execução (e-mail com o instante atual), então rodar duas vezes não dá 409.
set -uo pipefail

BASE="${1:-}"
BASE="${BASE%/}"
if [[ -z "$BASE" ]]; then
    echo "Uso: DEMO_PASSWORD=... bash $0 <url-do-frontend>" >&2
    exit 1
fi
if [[ -z "${DEMO_PASSWORD:-}" ]]; then
    echo "Defina DEMO_PASSWORD antes de rodar." >&2
    exit 1
fi

EMAIL_NOVO="demo-t3-$(date +%Y%m%d%H%M%S%N)@todolist.example"
TITULO="Smoke T3 $(date +%H%M%S)"
CORPO="$(mktemp)"
CABECALHOS="$(mktemp)"
trap 'rm -f "$CORPO" "$CABECALHOS"' EXIT

# --max-time 30: o primeiro request depois de ocioso pode pegar cold start de até três serviços.
falhas=0

# Uma linha OK/ERRO por passo. Sem -e: passo fora do esperado é resultado a reportar, não motivo para abortar.
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

# POST/GET JSON; imprime o status HTTP. $1=método $2=caminho $3=corpo (ou vazio) $4=token (opcional)
chamar() {
    local args=(-s -o "$CORPO" -D "$CABECALHOS" -w '%{http_code}' --max-time 30 -X "$1" "$BASE$2")
    [[ -n "$3" ]] && args+=(-H 'Content-Type: application/json' -d "$3")
    [[ -n "${4:-}" ]] && args+=(-H "Authorization: Bearer $4")
    curl "${args[@]}" || echo 000
}

echo "Origem: $BASE"
echo "Conta desta execução: $EMAIL_NOVO"
echo ""

# 0. Rota profunda do SPA -> 200 com o index.html do Angular (um F5 em /tasks), não 404.
status="$(chamar GET /tasks '')"
if [[ "$status" == "200" ]] && grep -qi '<app-root' "$CORPO"; then
    printf '  OK    %-56s HTTP %s\n' '0. GET /tasks (rota profunda do SPA)' "$status"
else
    printf '  ERRO  %-56s HTTP %s (esperado 200 com <app-root>)\n' '0. GET /tasks (rota profunda do SPA)' "$status"
    falhas=$((falhas + 1))
fi

# 1. Sem token -> 401.
status="$(chamar POST /api/tasks '{"title":"smoke sem token"}')"
relatar '1. POST /api/tasks sem token' "$status" 401 auth.unauthorized

# 2. Token lixo -> 401 (mesmo errorCode: a causa não é distinguível pelo cliente).
status="$(chamar POST /api/tasks '{"title":"smoke token lixo"}' token-lixo-arbitrario)"
relatar '2. POST /api/tasks com token lixo' "$status" 401 auth.unauthorized

# 3. Cadastro real. 4. Login da conta recém-cadastrada.
status="$(chamar POST /api/auth/register "{\"email\":\"$EMAIL_NOVO\",\"password\":\"$DEMO_PASSWORD\",\"displayName\":\"Demo T3\"}")"
relatar '3. POST /api/auth/register (conta nova)' "$status" 201
status="$(chamar POST /api/auth/login "{\"email\":\"$EMAIL_NOVO\",\"password\":\"$DEMO_PASSWORD\"}")"
relatar '4. POST /api/auth/login (conta recém-cadastrada)' "$status" 200

token="$(sed -n 's/.*"accessToken":"\([^"]*\)".*/\1/p' "$CORPO")"
[[ -n "$token" ]] || echo "        Sem accessToken no corpo do passo 4: os passos seguintes vão falhar em cascata."

# 2b. Token adulterado (último caractere trocado) -> 401: exercita a assinatura RS256, que o token lixo não exercita.
if [[ -n "$token" ]]; then
    if [[ "${token: -1}" == "A" ]]; then trocado="B"; else trocado="A"; fi
    status="$(chamar POST /api/tasks '{"title":"smoke token adulterado"}' "${token%?}$trocado")"
    relatar '2b. POST /api/tasks com token adulterado' "$status" 401 auth.unauthorized
fi

# 5. Título vazio -> 400 (validação na borda).
status="$(chamar POST /api/tasks '{"title":""}' "$token")"
relatar '5. POST /api/tasks com título vazio' "$status" 400

# 6. Título válido -> 201 com Location.
status="$(chamar POST /api/tasks "{\"title\":\"$TITULO\"}" "$token")"
relatar '6. POST /api/tasks válido' "$status" 201
if [[ "$status" == "201" ]] && grep -qi '^location:' "$CABECALHOS"; then
    printf '  OK    %-56s %s\n' '6b. Header Location presente' "$(grep -i '^location:' "$CABECALHOS" | tr -d '\r' | cut -d' ' -f2-)"
else
    printf '  ERRO  %-56s (ausente)\n' '6b. Header Location presente'
    falhas=$((falhas + 1))
fi

# 7. Listar -> 200 com a tarefa criada (prova a persistência pelo Tasks e o banco).
status="$(chamar GET /api/tasks '' "$token")"
relatar '7. GET /api/tasks lista a tarefa criada' "$status" 200
if ! grep -q "$TITULO" "$CORPO"; then
    printf '  ERRO  %-56s (título não veio na lista)\n' '7b. Tarefa criada aparece na lista'
    falhas=$((falhas + 1))
else
    printf '  OK    %-56s\n' '7b. Tarefa criada aparece na lista'
fi

echo ""
if [[ $falhas -eq 0 ]]; then
    echo "Tudo como esperado."
    exit 0
fi
echo "$falhas passo(s) fora do esperado. Veja os logs no Cloud Logging (README.md, seção de sintomas)."
exit 1
