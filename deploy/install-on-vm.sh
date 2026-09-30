#!/usr/bin/env bash
# Instala/atualiza os três serviços (Identity, Tasks, Gateway), o frontend
# estático e o nginx (BE-42, origem única HTTP da VM) na VM de aplicação.
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

for caminho in "$ORIGEM/publish/identity" "$ORIGEM/publish/tasks" "$ORIGEM/publish/gateway" "$ORIGEM/publish/frontend"; do
    if [[ ! -d "$caminho" ]]; then
        echo "Não encontrei $caminho — rode scripts/publish.ps1 e envie a pasta artifacts/ inteira." >&2
        exit 1
    fi
done

if [[ ! -f "$ORIGEM/nginx/todolist.conf" ]]; then
    echo "Não encontrei $ORIGEM/nginx/todolist.conf — rode scripts/publish.ps1 e envie a pasta artifacts/ inteira." >&2
    exit 1
fi

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

# Estáticos do Angular (BE-42): mesmo padrão de destino dos três serviços, um
# nível abaixo de /opt/todolist — é o "root" que deploy/nginx/todolist.conf
# espera. Dono root:root (não "todolist"): quem lê estes arquivos é o nginx,
# não um dos três processos .NET, e não precisam ser executáveis.
copiar_servico frontend
chown -R root:root "$DESTINO/frontend"
find "$DESTINO/frontend" -type d -exec chmod 755 {} +
find "$DESTINO/frontend" -type f -exec chmod 644 {} +

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
        # TROQUE_ESTA_SENHA cobre a connection string do identity.env e do
        # tasks.env (Onda E, T2: não existe mais UserStore__DemoUserPassword —
        # o seed de demonstração saiu, cadastro agora é real). Desde BE-40
        # (D-38/RS256) não existe mais
        # TROQUE_ESTA_CHAVE — a chave JWT não é uma variável de .env, é o par de
        # arquivos PEM gerado logo abaixo, na própria VM. O gateway.env não tem
        # segredo, mas passa pelo mesmo portão por consistência e para pegar o
        # caso de alguém copiar o .example sem editar os endereços.
        if grep -qE 'TROQUE_ESTA_SENHA' "$arquivo"; then
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

echo "==> Chave JWT RS256 (BE-40, D-38)"
# Idempotente e deliberadamente silenciosa: se o par já existe, não regera —
# trocar a chave invalida todo token já emitido (D-38, "Rotação de chave fica
# fora do escopo desta task"). Gerada SEMPRE na própria VM, nunca copiada de
# um ambiente de teste ou de outro lugar (BE-40, seção "Chaves").
JWT_DIR=/etc/todolist/jwt
if [[ ! -f "$JWT_DIR/private.pem" ]]; then
    mkdir -p "$JWT_DIR"
    chown root:root "$JWT_DIR"
    chmod 0755 "$JWT_DIR"

    openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out "$JWT_DIR/private.pem"
    openssl pkey -in "$JWT_DIR/private.pem" -pubout -out "$JWT_DIR/public.pem"

    # private.pem: só a unit do Identity a recebe, via LoadCredential= (as três
    # units rodam como o MESMO usuário todolist, então permissão por dono não
    # isolaria a chave do Gateway/Tasks — dono root e 0400 é o que isola).
    chown root:root "$JWT_DIR/private.pem"
    chmod 0400 "$JWT_DIR/private.pem"

    # public.pem não é segredo — só verifica, não assina. Modo 0444: legível
    # por qualquer processo da VM, lido direto pelo Gateway.
    chown root:root "$JWT_DIR/public.pem"
    chmod 0444 "$JWT_DIR/public.pem"

    echo "    par gerado em $JWT_DIR (nunca impresso, nunca versionado)"
else
    echo "    já existe: $JWT_DIR/private.pem (não regerado — trocar a chave invalida tokens emitidos)"
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

echo "==> nginx — origem única HTTP da VM (BE-42, D-40)"
# Entra por último, de propósito: só faz sentido publicar a porta 80 depois que
# os três serviços já respondem em 127.0.0.1, senão a primeira requisição real
# (inclusive do ensaio) passaria pelo nginx e encontraria um backend ainda de
# pé, igual à razão de ser da ordem Identity -> Tasks -> Gateway acima.
if ! command -v nginx >/dev/null 2>&1; then
    echo "    instalando pacote nginx"
    apt-get update -y
    apt-get install -y nginx
else
    echo "    já instalado: nginx"
fi

install -m 644 -o root -g root "$ORIGEM/nginx/todolist.conf" /etc/nginx/sites-available/todolist.conf
ln -sf /etc/nginx/sites-available/todolist.conf /etc/nginx/sites-enabled/todolist.conf

# O site "default" do pacote nginx também escuta na 80 e disputaria a porta com
# o nosso server_name _ (default_server) — remove se estiver ativo. Idempotente:
# se já não existir (segunda execução em diante), o `-e` só constata isso.
if [[ -e /etc/nginx/sites-enabled/default ]]; then
    rm -f /etc/nginx/sites-enabled/default
    echo "    site 'default' removido de sites-enabled (disputava a porta 80)"
fi

# nginx -t ANTES de qualquer reload/restart (CA-06) — uma config quebrada não
# deve nunca chegar a interromper um nginx que já estava servindo tráfego.
nginx -t

if systemctl is-active --quiet nginx; then
    systemctl reload nginx
else
    systemctl enable --now nginx
fi

echo ""
echo "==> Situação"
systemctl --no-pager --lines=0 status todolist-identity.service || true
systemctl --no-pager --lines=0 status todolist-tasks.service || true
systemctl --no-pager --lines=0 status todolist-gateway.service || true
systemctl --no-pager --lines=0 status nginx.service || true

echo ""
echo "Health checks:"
curl -fsS --max-time 5 http://127.0.0.1:5080/health && echo "  <- Identity" || echo "  Identity NÃO respondeu"
curl -fsS --max-time 5 http://127.0.0.1:5100/health && echo "  <- Tasks" || echo "  Tasks NÃO respondeu"
curl -fsS --max-time 5 http://127.0.0.1:8080/health && echo "  <- Gateway" || echo "  Gateway NÃO respondeu"
curl -fsS --max-time 5 -o /dev/null -w '%{http_code}' http://127.0.0.1:80/ && echo "  <- nginx (porta 80)" || echo "  nginx NÃO respondeu na porta 80"

echo ""
echo "Pronto. Verifique o fluxo completo com:  DEMO_PASSWORD=... ./smoke.sh"
