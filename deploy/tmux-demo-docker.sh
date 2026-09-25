#!/usr/bin/env bash
# Equivalente de tmux-demo.sh para o caminho DOCKER (Onda D). O original
# abre painéis com `journalctl -u todolist-*`, que não existe no mundo
# container — os quatro serviços de vida longa (identity, tasks, gateway,
# frontend) e o log de acesso viram, em vez disso, `docker compose logs -f
# <serviço>` a partir de /opt/todolist/docker/ (ver docker-compose.prod.yml).
#
#     ./tmux-demo-docker.sh
#
#     ┌───────────────────────┬──────────────────────────┐
#     │                       │  log do IDENTITY         │
#     │                       ├──────────────────────────┤
#     │   roteiro (demo.sh)   │  log do TASKS            │
#     │                       ├──────────────────────────┤
#     │                       │  log do GATEWAY          │
#     │                       ├──────────────────────────┤
#     │                       │  log do FRONTEND (nginx) │
#     └───────────────────────┴──────────────────────────┘
#
# Este arquivo NÃO SUBSTITUI tmux-demo.sh — aquele continua servindo o
# caminho systemd (plano B até 22/10). Os dois nunca deveriam ser usados na
# mesma apresentação, pela mesma razão que os dois caminhos de deploy não
# deveriam rodar juntos na VM (ver o comentário no topo de
# deploy/todolist.service): use o painel que corresponde ao que está
# realmente no ar.
#
# Diferença de conteúdo do painel "FRONTEND" em relação ao "NGINX" do
# original: lá, o nginx é um pacote da distro rodando FORA de qualquer
# container, sem log próprio no journal, então o painel acompanhava
# /var/log/nginx/access.log. Aqui, o nginx roda DENTRO do container
# `frontend` (frontend/Dockerfile, imagem nginx-unprivileged) — o `docker
# compose logs` dele já traz para o stdout do container tanto o access quanto
# o error log da imagem base, então um `docker compose logs -f frontend` já
# basta, sem precisar de `tail` num arquivo dentro do container.
#
# Se a sessão já existir, ele apenas reconecta (nada é recriado).
set -uo pipefail

SESSAO=demo-docker
COMPOSE_DIR=/opt/todolist/docker

if ! command -v tmux >/dev/null 2>&1; then
    echo "tmux não está instalado:  sudo apt-get install -y tmux" >&2
    exit 1
fi

if [[ ! -f "$COMPOSE_DIR/docker-compose.prod.yml" ]]; then
    echo "Não encontrei $COMPOSE_DIR/docker-compose.prod.yml — rode isto na VM," >&2
    echo "depois de deploy/install-docker-on-vm.sh e com a stack no ar." >&2
    exit 1
fi

if tmux has-session -t "$SESSAO" 2>/dev/null; then
    echo "Sessão '$SESSAO' já existe — reconectando."
    exec tmux attach -t "$SESSAO"
fi

DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# --no-log-prefix tira o nome do serviço/timestamp que o `docker compose
# logs` normalmente prefixa em cada linha — mesmo motivo do `-o cat` do
# jornal no script original: numa tela projetada, a linha precisa caber sem
# quebrar, senão o traceId (ou o que for) empurrado para a segunda linha some
# da correlação visual entre os painéis, principalmente vista da última
# fileira da sala. --since 0s equivale ao `-n 0` do original: painéis
# começam vazios, e tudo que aparecer foi causado durante a apresentação.
LOG_IDENTITY="cd $COMPOSE_DIR && docker compose -f docker-compose.prod.yml --env-file .env logs -f --no-log-prefix --since 0s identity"
LOG_TASKS="cd $COMPOSE_DIR && docker compose -f docker-compose.prod.yml --env-file .env logs -f --no-log-prefix --since 0s tasks"
LOG_GATEWAY="cd $COMPOSE_DIR && docker compose -f docker-compose.prod.yml --env-file .env logs -f --no-log-prefix --since 0s gateway"
LOG_FRONTEND="cd $COMPOSE_DIR && docker compose -f docker-compose.prod.yml --env-file .env logs -f --no-log-prefix --since 0s frontend"

tmux new-session -d -s "$SESSAO" -c "$DIR"
tmux split-window -h -t "$SESSAO:0.0" -c "$DIR"
tmux split-window -v -t "$SESSAO:0.1" -c "$DIR"
tmux split-window -v -t "$SESSAO:0.2" -c "$DIR"
tmux split-window -v -t "$SESSAO:0.3" -c "$DIR"

tmux send-keys -t "$SESSAO:0.1" "clear; echo '=== IDENTITY (container) ==='; $LOG_IDENTITY" C-m
tmux send-keys -t "$SESSAO:0.2" "clear; echo '=== TASKS (container) ==='; $LOG_TASKS" C-m
tmux send-keys -t "$SESSAO:0.3" "clear; echo '=== GATEWAY (container, so o frontend/nginx fala com ele) ==='; $LOG_GATEWAY" C-m
tmux send-keys -t "$SESSAO:0.4" "clear; echo '=== FRONTEND (nginx no container, origem unica HTTP, porta 80) ==='; $LOG_FRONTEND" C-m

# O painel do roteiro fica com metade da largura e é onde o cursor começa.
tmux send-keys -t "$SESSAO:0.0" "clear; echo 'Pronto. Rode:  DEMO_PASSWORD=... ./demo.sh'" C-m
tmux select-pane -t "$SESSAO:0.0"

echo "Sessão montada. Dicas: Ctrl+B seta = navegar | Ctrl+B d = sair sem matar | tmux attach -t $SESSAO = voltar"
sleep 2
exec tmux attach -t "$SESSAO"
