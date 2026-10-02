#!/usr/bin/env bash
# Verificação de fumaça do T2: a rota do SPA + os passos de BE-39, contra o
# nginx da VM — a única borda HTTP agora (D-32/D-40/BE-42). O Gateway passou
# a escutar só em 127.0.0.1:8080; quem responde na porta pública é o nginx,
# que serve o Angular e repassa /api/* ao Gateway. Roda na VM ou, por Git Bash,
# do notebook contra o IP externo (verificação "de fora").
#
#     DEMO_PASSWORD=... ./smoke.sh                  # contra http://127.0.0.1 (porta 80, via nginx)
#     DEMO_PASSWORD=... ./smoke.sh http://10.128.0.4
#     DEMO_PASSWORD=... DEMO_INACTIVE_EMAIL=inativo@todolist.example ./smoke.sh
#
# Rode isto depois de todo deploy, e de novo cerca de uma hora antes da
# apresentação. DEMO_PASSWORD é a senha da conta cadastrada pelo próprio
# script (passo 3) — nunca fixada neste script versionado; entra só por
# variável de ambiente.
#
# Onda E (T2): o seed de demonstração (DemoUserSeeder, dois usuários fixos em
# identity.users) foi removido — o cadastro é real agora (POST
# /api/auth/register), então este script cadastra uma conta nova a cada
# execução (e-mail com o instante atual, até o nanossegundo) em vez de logar
# direto com um usuário pronto. Isso é o que permite rodar o script duas
# vezes seguidas (ensaio, depois apresentação) sem um 409 de e-mail
# duplicado.
#
# O passo 7 (usuário inativo, RN-AUTH-09) não tem mais uma conta pronta: não
# existe rota para desativar usuário pela API, de propósito (superfície de
# negócio nova, fora de escopo). Se você já cadastrou uma conta e a desativou
# por UPDATE direto no banco (deploy/README.md, "No dia da apresentação"),
# informe o e-mail dela em DEMO_INACTIVE_EMAIL — sem isso, o passo é PULADO
# com um aviso explícito, nunca falha silenciosamente.
#
# Por que o padrão mudou de :8080 para sem porta (80): desde BE-42 o Gateway
# não é mais alcançável de fora do 127.0.0.1 da própria VM — verificar contra
# :8080 aqui dentro continuaria "funcionando" (loopback) mas deixaria de
# provar o que a apresentação de fato usa, que é o caminho via nginx.
set -uo pipefail

BASE="${1:-http://127.0.0.1}"

if [[ -z "${DEMO_PASSWORD:-}" ]]; then
    echo "Defina DEMO_PASSWORD antes de rodar:" >&2
    echo "  DEMO_PASSWORD=sua-senha-de-demo ./smoke.sh" >&2
    exit 1
fi

# E-mail novo a cada execução — cadastro real, não mais o seed fixo removido
# na Onda E. %N (nanossegundos) evita colisão mesmo rodando o script duas
# vezes no mesmo segundo.
EMAIL_NOVO="demo-t2-$(date +%Y%m%d%H%M%S%N)@todolist.example"
EMAIL_INATIVO="${DEMO_INACTIVE_EMAIL:-}"

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

echo "Origem única (nginx): $BASE"
echo "Conta desta execução: $EMAIL_NOVO"
echo ""

# 0. Rota profunda do SPA -> 200 com o index.html do Angular, não 404
# (BE-42 CA-01). É o teste mais fácil de esquecer porque navegar DENTRO da
# aplicação nunca aciona esse caminho — só um F5 numa rota como /tasks o
# faz, e é exatamente isso que este passo simula sem precisar de navegador.
status="$(curl -s -o "$CORPO" -w '%{http_code}' --max-time 15 "$BASE/tasks")" || status=000
corpo_spa="$(cat "$CORPO" 2>/dev/null || true)"
if [[ "$status" == "200" ]] && grep -qi '<app-root' <<<"$corpo_spa"; then
    printf '  OK    %-56s HTTP %s\n' '0. GET /tasks (rota profunda do SPA)' "$status"
else
    printf '  ERRO  %-56s HTTP %s (esperado 200 com <app-root> no corpo)\n' '0. GET /tasks (rota profunda do SPA)' "$status"
    falhas=$((falhas + 1))
fi

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

# 3. Cadastro real (POST /api/auth/register) — substitui o login direto
# contra o usuário do seed (removido, Onda E). E-mail novo a cada execução,
# então rodar o script de novo não esbarra num 409.
status="$(curl -s -o "$CORPO" -w '%{http_code}' --max-time 15 \
    -X POST "$BASE/api/auth/register" -H 'Content-Type: application/json' \
    -d "{\"email\":\"$EMAIL_NOVO\",\"password\":\"$DEMO_PASSWORD\",\"displayName\":\"Demo T2\"}")" || status=000
relatar '3. POST /api/auth/register (conta nova)' "$status" 201

# 4. Login da conta recém-cadastrada -> 200 com accessToken/expiresAt.
status="$(curl -s -o "$CORPO" -w '%{http_code}' --max-time 15 \
    -X POST "$BASE/api/auth/login" -H 'Content-Type: application/json' \
    -d "{\"email\":\"$EMAIL_NOVO\",\"password\":\"$DEMO_PASSWORD\"}")" || status=000
relatar '4. POST /api/auth/login (conta recém-cadastrada)' "$status" 200

token="$(extrair_token)"
if [[ -z "$token" ]]; then
    echo "        Sem accessToken no corpo do passo 4 — os passos 5 e 6 vão falhar em cascata."
fi

# 2b. Token adulterado -> 401 (um JWT real, do passo 4, com o último
# caractere trocado — exercita a verificação de assinatura RS256 do
# AddJwtBearer, BE-40, o que "token lixo" no passo 2 não faz). Só roda se
# o passo 4 de fato devolveu um token.
if [[ -n "$token" ]]; then
    ultimo="${token: -1}"
    if [[ "$ultimo" == "A" ]]; then substituto="B"; else substituto="A"; fi
    token_adulterado="${token%?}${substituto}"
    status="$(curl -s -o "$CORPO" -w '%{http_code}' --max-time 15 \
        -X POST "$BASE/api/tasks" -H 'Content-Type: application/json' \
        -H "Authorization: Bearer $token_adulterado" \
        -d '{"title":"smoke token adulterado"}')" || status=000
    relatar '2b. POST /api/tasks com token adulterado' "$status" 401 auth.unauthorized
fi

# 5. Token válido, título vazio -> 400 (validação na borda, antes de qualquer
# chamada gRPC ao Tasks).
status="$(curl -s -o "$CORPO" -w '%{http_code}' --max-time 15 \
    -X POST "$BASE/api/tasks" -H 'Content-Type: application/json' \
    -H "Authorization: Bearer $token" \
    -d '{"title":""}')" || status=000
relatar '5. POST /api/tasks com título vazio' "$status" 400

# 6. Token válido, título válido -> 201 com Location. O status e o Location
# são conferidos em duas linhas separadas porque são duas evidências
# diferentes (BE-39 CA-01: "REST público" + "tradução JSON -> gRPC").
status="$(curl -s -o "$CORPO" -D "$CABECALHOS" -w '%{http_code}' --max-time 15 \
    -X POST "$BASE/api/tasks" -H 'Content-Type: application/json' \
    -H "Authorization: Bearer $token" \
    -d '{"title":"Verificacao smoke.sh do T2"}')" || status=000
relatar '6. POST /api/tasks válido' "$status" 201

if [[ "$status" == "201" ]] && grep -qi '^location:' "$CABECALHOS"; then
    local_location="$(grep -i '^location:' "$CABECALHOS" | tr -d '\r' | cut -d' ' -f2-)"
    printf '  OK    %-56s %s\n' '6b. Header Location presente' "$local_location"
else
    printf '  ERRO  %-56s (ausente)\n' '6b. Header Location presente'
    falhas=$((falhas + 1))
fi

# 7. Usuário inativo (RN-AUTH-09) — não há mais um usuário pronto para isto
# (o seed saiu, Onda E, e não existe rota para desativar conta pela API, de
# propósito). Só roda se DEMO_INACTIVE_EMAIL foi informada, apontando para
# uma conta cadastrada e depois desativada por UPDATE direto no banco
# (deploy/README.md, "No dia da apresentação"). Sem isso, PULADO com aviso —
# nunca falha silenciosamente, nunca finge sucesso.
if [[ -z "$EMAIL_INATIVO" ]]; then
    printf '  PULADO %-55s (defina DEMO_INACTIVE_EMAIL)\n' '7. POST /api/auth/login (usuário inativo)'
    echo "         Cadastre uma conta e desative-a por SQL (deploy/README.md, 'No dia da apresentação'),"
    echo "         depois rode de novo com DEMO_INACTIVE_EMAIL=<email-dessa-conta>."
else
    status="$(curl -s -o "$CORPO" -w '%{http_code}' --max-time 15 \
        -X POST "$BASE/api/auth/login" -H 'Content-Type: application/json' \
        -d "{\"email\":\"$EMAIL_INATIVO\",\"password\":\"$DEMO_PASSWORD\"}")" || status=000
    relatar '7. POST /api/auth/login (usuário inativo)' "$status" 401 auth.invalid_credentials
fi

echo ""
if [[ $falhas -eq 0 ]]; then
    echo "Tudo como esperado."
    echo ""
    echo "Evidência do traceId correlacionado (passo 6, os três serviços — o nginx repassa o"
    echo "traceparent intacto, BE-42/D-40, então ele não aparece como um quarto serviço na correlação)."
    echo "  sudo docker compose -f /opt/todolist/docker/docker-compose.prod.yml --env-file /opt/todolist/docker/.env \\"
    echo "    logs --since 2m gateway tasks identity | grep -E 'ValidateToken|CreateTask|ValidateUser'"
    exit 0
fi

echo "$falhas passo(s) fora do esperado. Logs para diagnóstico:"
echo ""
echo "  sudo docker compose -f /opt/todolist/docker/docker-compose.prod.yml --env-file /opt/todolist/docker/.env \\"
echo "    logs --tail 150 gateway tasks identity"
exit 1
