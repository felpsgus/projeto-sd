#!/usr/bin/env bash
# Monta a tela da apresentação em quatro painéis e entra nela.
#
#     ./tmux-demo.sh
#
#     ┌───────────────────────┬──────────────────────────┐
#     │                       │  log do IDENTITY         │
#     │   roteiro (demo.sh)   ├──────────────────────────┤
#     │                       │  log do TASKS            │
#     │                       ├──────────────────────────┤
#     │                       │  log do GATEWAY          │
#     └───────────────────────┴──────────────────────────┘
#
# O layout é montado por script, e não com Ctrl+B na hora, por um motivo prático:
# dividir painel ao vivo, com a turma esperando, é onde se perde meio minuto dos
# cinco — e às vezes o painel abre no lugar errado.
#
# Se a sessão já existir, ele apenas reconecta (nada é recriado).
set -uo pipefail

SESSAO=demo

if ! command -v tmux >/dev/null 2>&1; then
    echo "tmux não está instalado:  sudo apt-get install -y tmux" >&2
    exit 1
fi

if tmux has-session -t "$SESSAO" 2>/dev/null; then
    echo "Sessão '$SESSAO' já existe — reconectando."
    exec tmux attach -t "$SESSAO"
fi

DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# -o cat tira o carimbo de data/hora e o nome do host de cada linha: numa tela
# projetada, cada linha precisa caber sem quebrar — senão o dado que se quer
# mostrar (traceId, errorCode, o que for) vai para a segunda linha e a
# correlação entre os painéis deixa de ser visível da última fileira da sala.
# -n 0 não mostra histórico: os painéis começam vazios, e tudo que aparecer
# durante a apresentação foi causado por você.
LOG_IDENTITY='sudo journalctl -fu todolist-identity -n 0 -o cat'
LOG_TASKS='sudo journalctl -fu todolist-tasks -n 0 -o cat'
LOG_GATEWAY='sudo journalctl -fu todolist-gateway -n 0 -o cat'

tmux new-session -d -s "$SESSAO" -c "$DIR"
tmux split-window -h -t "$SESSAO:0.0" -c "$DIR"
tmux split-window -v -t "$SESSAO:0.1" -c "$DIR"
tmux split-window -v -t "$SESSAO:0.2" -c "$DIR"

tmux send-keys -t "$SESSAO:0.1" "clear; echo '=== IDENTITY (servidor gRPC) ==='; $LOG_IDENTITY" C-m
tmux send-keys -t "$SESSAO:0.2" "clear; echo '=== TASKS (servidor gRPC) ==='; $LOG_TASKS" C-m
tmux send-keys -t "$SESSAO:0.3" "clear; echo '=== GATEWAY (origem unica HTTP) ==='; $LOG_GATEWAY" C-m

# O painel do roteiro fica com metade da largura e é onde o cursor começa.
tmux send-keys -t "$SESSAO:0.0" "clear; echo 'Pronto. Rode:  DEMO_PASSWORD=... ./demo.sh'" C-m
tmux select-pane -t "$SESSAO:0.0"

echo "Sessão montada. Dicas: Ctrl+B seta = navegar | Ctrl+B d = sair sem matar | tmux attach -t $SESSAO = voltar"
sleep 2
exec tmux attach -t "$SESSAO"
