#!/usr/bin/env bash
# Verificação de fumaça dos quatro desfechos de POST /api/tasks — o equivalente
# de scripts/demo-curl.ps1, para rodar dentro da VM.
#
#     ./smoke.sh                      # contra 127.0.0.1:5100
#     ./smoke.sh http://10.128.0.4:5100
#
# Rode isto depois de todo deploy, e de novo cerca de uma hora antes da
# apresentação. É a diferença entre descobrir um problema com tempo de corrigir e
# descobrir na frente da turma.
set -uo pipefail

BASE="${1:-http://127.0.0.1:5100}"

DONO_ATIVO=10000000-0000-0000-0000-000000000001
DONO_INATIVO=10000000-0000-0000-0000-000000000002
DONO_INEXISTENTE=99999999-9999-9999-9999-999999999999

falhas=0

verificar() {
    local cenario="$1" user_id="$2" esperado_status="$3" esperado_code="${4:-}"

    local args=(-s -o /tmp/smoke-body -w '%{http_code}' --max-time 15
                -X POST "$BASE/api/tasks" -H 'Content-Type: application/json'
                -d '{"title":"smoke test"}')
    [[ -n "$user_id" ]] && args+=(-H "X-User-Id: $user_id")

    local status
    status="$(curl "${args[@]}")" || status=000
    local corpo
    corpo="$(cat /tmp/smoke-body 2>/dev/null || true)"

    local ok=1
    [[ "$status" == "$esperado_status" ]] || ok=0
    if [[ -n "$esperado_code" ]] && ! grep -q "\"$esperado_code\"" <<<"$corpo"; then ok=0; fi

    if [[ $ok -eq 1 ]]; then
        printf '  OK    %-52s HTTP %s %s\n' "$cenario" "$status" "$esperado_code"
    else
        printf '  ERRO  %-52s HTTP %s (esperado %s %s)\n' "$cenario" "$status" "$esperado_status" "$esperado_code"
        printf '        %s\n' "$corpo"
        falhas=$((falhas + 1))
    fi
}

echo "Tasks Service: $BASE"
echo ""

verificar 'dono ativo -> cria'              "$DONO_ATIVO"       201
verificar 'dono inexistente -> Identity nega' "$DONO_INEXISTENTE" 404 task.owner_not_found
verificar 'dono inativo -> Identity nega'   "$DONO_INATIVO"     409 task.owner_inactive
verificar 'sem X-User-Id -> validacao'      ''                  400

echo ""
if [[ $falhas -eq 0 ]]; then
    echo "Tudo como esperado."
    echo ""
    echo "Evidencia da chamada gRPC (mesmo traceId nos dois servicos):"
    echo "  sudo journalctl -u todolist-tasks -u todolist-identity --since '2 min ago' | grep ValidateUser"
    exit 0
fi

echo "$falhas caminho(s) fora do esperado."
echo "  sudo journalctl -u todolist-tasks -u todolist-identity -n 80 --no-pager"
exit 1
