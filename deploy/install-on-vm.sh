#!/usr/bin/env bash
# Instala/atualiza os três serviços (Identity, Tasks, Gateway) na VM de aplicação.
#
# Rode NA VM, a partir do diretório onde os artefatos foram descarregados
# (ver deploy/README.md):
#
#     sudo ./install-on-vm.sh
#
# Idempotente: rodar de novo é o jeito normal de reimplantar uma versão nova.
# Não toca em /etc/todolist/*.env — os segredos são criados uma vez, à mão, e
# sobrevivem a qualquer redeploy.
set -euo pipefail

ORIGEM="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DESTINO=/opt/todolist
USUARIO=todolist

if [[ $EUID -ne 0 ]]; then
    echo "Rode com sudo." >&2
    exit 1
fi

for caminho in "$ORIGEM/publish/identity" "$ORIGEM/publish/tasks" "$ORIGEM/publish/gateway"; do
    if [[ ! -d "$caminho" ]]; then
        echo "Não encontrei $caminho — rode scripts/publish.ps1 e envie a pasta artifacts/ inteira." >&2
        exit 1
    fi
done

echo "==> Usuário de serviço"
# --system: sem senha, sem login, sem home. O serviço não precisa de nenhum dos
# três, e cada um deles seria superfície de ataque de graça.
if ! id -u "$USUARIO" >/dev/null 2>&1; then
    useradd --system --no-create-home --shell /usr/sbin/nologin "$USUARIO"
    echo "    criado: $USUARIO"
else
    echo "    já existe: $USUARIO"
fi

echo "==> Parando serviços (se já estiverem instalados)"
systemctl stop todolist-gateway.service 2>/dev/null || true
systemctl stop todolist-tasks.service 2>/dev/null || true
systemctl stop todolist-identity.service 2>/dev/null || true

echo "==> Copiando binários para $DESTINO"
mkdir -p "$DESTINO"

# O destino é apagado antes de receber a versão nova: um arquivo que saiu do
# pacote precisa sair da VM também. Sem isso, uma DLL órfã de uma versão antiga
# pode ser carregada e o sintoma é incompreensível.
#
# rsync --delete faria o mesmo com mais elegância, mas ele não está garantido em
# toda imagem do Compute Engine — e descobrir isso no meio do deploy, na véspera,
# não é o momento. cp -a está em qualquer lugar.
copiar_servico() {
    local nome="$1"
    rm -rf "${DESTINO:?}/$nome"
    mkdir -p "$DESTINO/$nome"
    cp -a "$ORIGEM/publish/$nome/." "$DESTINO/$nome/"
}

copiar_servico identity
copiar_servico tasks
copiar_servico gateway

chmod +x "$DESTINO/identity/TodoList.Identity.Api" "$DESTINO/tasks/TodoList.Tasks.Api" "$DESTINO/gateway/TodoList.Gateway.Api"
chown -R "$USUARIO:$USUARIO" "$DESTINO"

echo "==> Verificando os arquivos de ambiente"
mkdir -p /etc/todolist
faltando=0
for arquivo in /etc/todolist/identity.env /etc/todolist/tasks.env /etc/todolist/gateway.env; do
    if [[ ! -f "$arquivo" ]]; then
        echo "    FALTANDO: $arquivo" >&2
        faltando=1
    else
        chmod 600 "$arquivo"
        chown root:root "$arquivo"
        # TROQUE_ESTA_SENHA cobre a connection string e a UserStore__DemoUserPassword
        # do identity.env; TROQUE_ESTA_CHAVE cobre a Jwt__SigningKey. O gateway.env
        # não tem segredo (D-31), mas passa pelo mesmo portão por consistência e
        # para pegar o caso de alguém copiar o .example sem editar os endereços.
        if grep -qE 'TROQUE_ESTA_SENHA|TROQUE_ESTA_CHAVE' "$arquivo"; then
            echo "    ATENÇÃO: $arquivo ainda tem um valor placeholder." >&2
            faltando=1
        fi
    fi
done

if [[ $faltando -eq 1 ]]; then
    echo "" >&2
    echo "Crie os arquivos de ambiente a partir de deploy/*.env.example antes de continuar." >&2
    exit 1
fi

echo "==> Instalando units do systemd"
install -m 644 -o root -g root "$ORIGEM/todolist-identity.service" /etc/systemd/system/
install -m 644 -o root -g root "$ORIGEM/todolist-tasks.service" /etc/systemd/system/
install -m 644 -o root -g root "$ORIGEM/todolist-gateway.service" /etc/systemd/system/
systemctl daemon-reload

echo "==> Habilitando e subindo — Identity, depois Tasks, depois Gateway"
systemctl enable --now todolist-identity.service
# O Tasks não falha ao iniciar sem o Identity: ele só devolve 503 na primeira
# criação (D-28). A espera aqui é para que o primeiro teste depois do deploy não
# dê 503 e pareça defeito.
for _ in $(seq 1 30); do
    if curl -fsS --max-time 2 http://127.0.0.1:5080/health >/dev/null 2>&1; then break; fi
    sleep 1
done

systemctl enable --now todolist-tasks.service
for _ in $(seq 1 30); do
    if curl -fsS --max-time 2 http://127.0.0.1:5100/health >/dev/null 2>&1; then break; fi
    sleep 1
done

# Mesma lógica: o Gateway não falha ao subir sem os back-ends (Wants=, não
# Requires=), mas subir os dois antes evita que as primeiras requisições reais
# — inclusive as do ensaio — encontrem 503 por um back-end ainda inicializando.
systemctl enable --now todolist-gateway.service
for _ in $(seq 1 30); do
    if curl -fsS --max-time 2 http://127.0.0.1:8080/health >/dev/null 2>&1; then break; fi
    sleep 1
done

echo ""
echo "==> Situação"
systemctl --no-pager --lines=0 status todolist-identity.service || true
systemctl --no-pager --lines=0 status todolist-tasks.service || true
systemctl --no-pager --lines=0 status todolist-gateway.service || true

echo ""
echo "Health checks:"
curl -fsS --max-time 5 http://127.0.0.1:5080/health && echo "  <- Identity" || echo "  Identity NÃO respondeu"
curl -fsS --max-time 5 http://127.0.0.1:5100/health && echo "  <- Tasks" || echo "  Tasks NÃO respondeu"
curl -fsS --max-time 5 http://127.0.0.1:8080/health && echo "  <- Gateway" || echo "  Gateway NÃO respondeu"

echo ""
echo "Pronto. Verifique o fluxo completo com:  DEMO_PASSWORD=... ./smoke.sh"
