#!/usr/bin/env bash
# Prepara a VM para o caminho Docker (uma VM só, tudo em container, Postgres
# no Cloud SQL). Instala Docker Engine, monta
# /opt/todolist/docker/ com o compose de produção, garante a chave JWT com a
# permissão que o container exige, e instala a unit deploy/todolist.service.
#
# Rode NA VM, a partir do diretório onde os arquivos abaixo foram copiados
# (ver deploy/README.md, seção 5 — `gcloud compute scp`):
#
#     sudo ./install-docker-on-vm.sh
#
# Espera encontrar, ao lado deste script:
#   docker-compose.prod.yml
#   todolist.env.example
#   todolist.service
#   sql/01-identity.sql
#   sql/02-tasks.sql
#
# Idempotente: rodar de novo é o jeito normal de preparar a VM outra vez (ex.:
# depois de recriá-la) ou de atualizar o compose/unit sem perder o que já foi
# configurado à mão (.env preenchido, chave JWT gerada).
#
set -euo pipefail

ORIGEM="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DESTINO=/opt/todolist/docker
JWT_DIR=/etc/todolist/jwt

if [[ $EUID -ne 0 ]]; then
    echo "Rode com sudo." >&2
    exit 1
fi

for arquivo in "$ORIGEM/docker-compose.prod.yml" "$ORIGEM/todolist.env.example" "$ORIGEM/todolist.service" \
    "$ORIGEM/sql/01-identity.sql" "$ORIGEM/sql/02-tasks.sql"; do
    if [[ ! -f "$arquivo" ]]; then
        echo "Não encontrei $arquivo — copie deploy/docker-compose.prod.yml, deploy/todolist.env.example," >&2
        echo "deploy/todolist.service e artifacts/sql/*.sql (scripts/new-migrations-sql.ps1 gera estes últimos)" >&2
        echo "para este diretório antes de rodar (ver deploy/README.md)." >&2
        exit 1
    fi
done

echo "==> Docker Engine + plugin compose"
# O pacote docker.io/docker-compose da distro (apt padrão do Debian/Ubuntu) é
# tipicamente uma versão velha, sem o subcomando `docker compose` (v2, sem
# hífen) que docker-compose.prod.yml/todolist.service usam — por isso o
# repositório OFICIAL da Docker, não `apt-get install docker.io`.
if command -v docker >/dev/null 2>&1 && docker compose version >/dev/null 2>&1; then
    echo "    já instalado: $(docker --version), $(docker compose version --short 2>/dev/null || echo 'compose ok')"
else
    echo "    instalando (repositório oficial docker.com)"
    apt-get update -y
    apt-get install -y ca-certificates curl gnupg

    install -m 0755 -d /etc/apt/keyrings
    # -n: não sobrescreve uma chave já baixada numa execução anterior — não é
    # segredo nem configuração do usuário, mas idempotência sem trabalho
    # (re-baixar) à toa não faz mal nenhum aqui.
    if [[ ! -f /etc/apt/keyrings/docker.asc ]]; then
        curl -fsSL https://download.docker.com/linux/debian/gpg -o /etc/apt/keyrings/docker.asc
        chmod a+r /etc/apt/keyrings/docker.asc
    fi

    # `. /etc/os-release` dá $VERSION_CODENAME e $ID (debian/ubuntu) — o
    # Compute Engine das imagens Debian padrão do GCP é isso mesmo; se a VM
    # for Ubuntu, o mesmo repositório funciona trocando "debian" por "ubuntu"
    # (o snippet abaixo já resolve isso via $ID).
    # shellcheck disable=SC1091
    . /etc/os-release
    echo \
        "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/${ID} ${VERSION_CODENAME} stable" \
        > /etc/apt/sources.list.d/docker.list

    apt-get update -y
    apt-get install -y docker-ce docker-ce-cli containerd.io docker-compose-plugin

    systemctl enable --now docker
    echo "    instalado: $(docker --version)"
fi

echo "==> /opt/todolist/docker/"
mkdir -p "$DESTINO/artifacts/sql"

install -m 644 -o root -g root "$ORIGEM/docker-compose.prod.yml" "$DESTINO/docker-compose.prod.yml"
install -m 644 -o root -g root "$ORIGEM/sql/01-identity.sql" "$DESTINO/artifacts/sql/01-identity.sql"
install -m 644 -o root -g root "$ORIGEM/sql/02-tasks.sql" "$DESTINO/artifacts/sql/02-tasks.sql"

# .env: NUNCA sobrescrever um já preenchido — é exatamente o tipo de coisa que
# um instalador não pode fazer (apagaria IP do Cloud SQL, senha e tag de
# imagem de uma configuração de produção já em uso). Só copia o .example
# quando o .env ainda não existe.
if [[ -f "$DESTINO/.env" ]]; then
    echo "    já existe: $DESTINO/.env (não sobrescrito — edite à mão se precisar)"
else
    install -m 600 -o root -g root "$ORIGEM/todolist.env.example" "$DESTINO/.env"
    echo "    criado: $DESTINO/.env a partir de todolist.env.example — PREENCHA os placeholders"
    echo "    (IP privado do Cloud SQL, senha, IMAGE_TAG) antes de subir a stack."
fi

echo "==> Chave JWT RS256 (D-38)"
# A chave mora em /etc/todolist/jwt/ no host (é o caminho que o `secrets:` do
# docker-compose.prod.yml monta nos containers). Nunca regenerada se já
# existir — trocar a chave invalida todo token já emitido.
if [[ ! -f "$JWT_DIR/private.pem" ]]; then
    mkdir -p "$JWT_DIR"
    chown root:root "$JWT_DIR"
    chmod 0755 "$JWT_DIR"

    openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out "$JWT_DIR/private.pem"
    openssl pkey -in "$JWT_DIR/private.pem" -pubout -out "$JWT_DIR/public.pem"
    echo "    par gerado em $JWT_DIR (nunca impresso, nunca versionado)"
else
    echo "    já existe: $JWT_DIR/private.pem (não regerado — trocar a chave invalida tokens emitidos)"
fi

# A ARMADILHA CENTRAL DO CAMINHO DOCKER (documentada por extenso em
# deploy/docker-compose.prod.yml, seção `secrets:` — leia lá antes de mexer
# aqui): sem Docker Swarm, um `secrets:` de compose com `file:` é um bind
# mount comum. O arquivo chega DENTRO do container com o MESMO dono e MESMO
# modo que tem no HOST — ninguém intermedeia a leitura como o systemd faz com
# `LoadCredential=`. O processo do Identity roda como uid 1654 (usuário `app`
# da imagem mcr.microsoft.com/dotnet/aspnet:10.0, ativado por `USER $APP_UID`
# nos três Dockerfiles em src/*/*.Api/Dockerfile) — NÃO uma constante do .NET,
# é um detalhe de implementação da imagem base da Microsoft, que pode mudar
# numa atualização futura da imagem. Se isso acontecer, o sintoma vai ser
# idêntico ao de hoje com dono errado (Identity não sobe, "permission denied"
# ao ler o PEM, ou um erro genérico do runtime sem menção a arquivo nenhum) —
# nada vai apontar para "o uid da imagem mudou". Reconfira rodando:
#
#   docker run --rm mcr.microsoft.com/dotnet/aspnet:10.0 id app
#
# private.pem root:root 0400 (o padrão para chave privada) fica ILEGÍVEL para
# uid 1654 aqui — por isso o dono muda para 1654, mantendo 0400 (só esse
# uid específico consegue ler, ninguém mais — nem root por padrão de leitura
# direta de outro processo, embora root sempre possa trocar permissão de
# novo; o ponto é que nenhum OUTRO processo comum da VM consegue). public.pem
# não é segredo (só verifica, não assina): 0444 está bem, e nem precisa do
# chown por dono específico, mas alinhamos por consistência com o par.
chown 1654:1654 "$JWT_DIR/private.pem"
chmod 0400 "$JWT_DIR/private.pem"
chown 1654:1654 "$JWT_DIR/public.pem"
chmod 0444 "$JWT_DIR/public.pem"
echo "    permissão aplicada: private.pem 1654:1654 0400, public.pem 1654:1654 0444"
echo "    (uid 1654 = usuário 'app' da imagem aspnet:10.0 — reconfira com:"
echo "     docker run --rm mcr.microsoft.com/dotnet/aspnet:10.0 id app)"

echo "==> Autenticação do Docker no Artifact Registry (como ROOT — leia por quê)"
# QUEM RODA O PULL É A UNIT todolist.service, Type=oneshot, NO BOOT, COMO
# ROOT (ver deploy/todolist.service) — não a pessoa que fez o SSH. O
# credential helper do Docker (docker-credential-gcloud) é configurado em
# ~/.docker/config.json DE QUEM RODA O `docker`/`docker compose`. Se você
# rodar `gcloud auth configure-docker` como seu usuário comum (o normal num
# `sudo ./install-docker-on-vm.sh` onde só o script roda elevado), ele grava
# em /home/<voce>/.docker/config.json — um arquivo que o ROOT NUNCA LÊ. O
# sintoma é traiçoeiro: você testa `docker pull` manualmente como você mesmo
# e funciona; a unit continua falhando no próximo boot, silenciosamente,
# porque autentica com uma identidade diferente. Por isso este passo roda
# como root deliberadamente — o script inteiro já exige sudo (checado no
# topo), então `gcloud` aqui já é invocado como root e escreve em
# /root/.docker/config.json, que é exatamente o arquivo que a unit lê.
if ! command -v gcloud >/dev/null 2>&1; then
    echo "gcloud não encontrado nesta VM — o helper docker-credential-gcloud vem do" >&2
    echo "Google Cloud SDK. Instale antes de continuar:" >&2
    echo "  https://cloud.google.com/sdk/docs/install-sdk#linux" >&2
    exit 1
fi

gcloud auth configure-docker us-central1-docker.pkg.dev --quiet
echo "    escrito em /root/.docker/config.json (é ESTE arquivo que a unit lê, rodando como root)"

# Sinal POSITIVO de credencial, independente de qualquer imagem existir.
#
# Por que este teste existe além do `docker pull` logo abaixo: o pull mistura
# duas perguntas numa resposta só ("autentiquei?" e "a imagem existe?"), e a
# distinção é feita lá por texto de mensagem de erro do registry — o que exige
# assumir que o Artifact Registry sempre responde "denied" (e nunca "não
# encontrado") para quem não tem permissão de leitura. Se essa premissa
# estiver errada num caso de borda, uma falha de AUTENTICAÇÃO se disfarçaria
# de "imagem ainda não publicada", que é um aviso benigno — e a unit iria para
# o boot quebrada mesmo assim.
#
# `docker-credential-gcloud get` responde só a primeira pergunta: ele pede um
# token ao metadata server desta VM para o host do registry. Se isso funciona,
# a credencial está de pé, e qualquer "não encontrado" do pull abaixo é
# genuinamente sobre a imagem, não sobre permissão.
echo "==> Verificando a credencial do Docker (independe de a imagem existir)"
if echo "us-central1-docker.pkg.dev" | docker-credential-gcloud get >/dev/null 2>&1; then
    echo "    OK — docker-credential-gcloud obteve token para us-central1-docker.pkg.dev."
else
    echo "    AVISO: docker-credential-gcloud NÃO conseguiu obter token para" >&2
    echo "    us-central1-docker.pkg.dev nesta VM. Isso aponta para credencial/escopo," >&2
    echo "    não para imagem faltando — leia os dois remédios impressos abaixo se o" >&2
    echo "    pull também falhar, e desconfie de qualquer 'imagem não encontrada'." >&2
fi

echo "==> Verificando se o pull REALMENTE autentica (antes de habilitar a unit)"
# Falhar aqui, na instalação, com uma mensagem clara, é infinitamente melhor
# do que falhar silenciosamente no boot (a unit sobe em background, ninguém
# olha, e o sintoma vira "a VM subiu, o Docker está no ar, e a stack inteira
# está parada" — sem nenhum indício de que a causa é autenticação).
#
# Lemos IMAGE_REGISTRY/IMAGE_TAG do .env já instalado acima (com fallback
# para os valores padrão de todolist.env.example) — não presumimos que as
# quatro imagens já foram publicadas: a primeira instalação da VM roda antes
# do primeiro `scripts/publish-images.ps1` na prática, então "imagem não
# encontrada" é um resultado ESPERADO nesse momento, distinto de "não
# autenticou" (ver a distinção de mensagens abaixo).
IMAGE_REGISTRY="$(grep -m1 '^IMAGE_REGISTRY=' "$DESTINO/.env" 2>/dev/null | cut -d= -f2-)"
IMAGE_TAG="$(grep -m1 '^IMAGE_TAG=' "$DESTINO/.env" 2>/dev/null | cut -d= -f2-)"
IMAGE_REGISTRY="${IMAGE_REGISTRY:-us-central1-docker.pkg.dev/sd-26-2/todolist}"
IMAGE_TAG="${IMAGE_TAG:-latest}"
IMAGEM_TESTE="$IMAGE_REGISTRY/todolist-gateway:$IMAGE_TAG"

SAIDA_PULL="$(docker pull "$IMAGEM_TESTE" 2>&1)" && PULL_OK=1 || PULL_OK=0

if [[ $PULL_OK -eq 1 ]]; then
    echo "    OK — pull de $IMAGEM_TESTE autenticou e a imagem existe."
elif grep -qiE 'manifest unknown|not found: manifest|no such (image|tag)' <<<"$SAIDA_PULL"; then
    # "Não encontrou a imagem" normalmente só chega depois de passar pela
    # autorização — mas essa é uma premissa sobre o comportamento do registry,
    # não uma certeza. Quem confirma que a credencial está de pé é o teste do
    # docker-credential-gcloud logo acima; se AQUELE avisou que não conseguiu
    # token, trate este "não encontrada" como suspeito e vá direto aos dois
    # remédios do bloco seguinte.
    echo "    AVISO: pull de $IMAGEM_TESTE não encontrou a imagem (ainda não publicada?)." >&2
    echo "    A autenticação parece OK (o erro é 'não encontrada', não 'não autorizada')." >&2
    echo "    Isto é esperado antes do primeiro scripts/publish-images.ps1. Depois de" >&2
    echo "    publicar a primeira imagem, rode este script de novo para confirmar de vez." >&2
else
    # Qualquer outra coisa (unauthorized, denied, authentication required,
    # ou uma mensagem que não reconhecemos) é tratada como falha de
    # autenticação por padrão — errar para o lado de "trave e avise" é mais
    # seguro aqui do que assumir sucesso e deixar a unit falhar no boot.
    cat >&2 <<EOF

FALHA ao tentar puxar $IMAGEM_TESTE:
$SAIDA_PULL

Isto se repetiria SILENCIOSAMENTE no próximo boot — a unit todolist.service roda
"docker compose ... up -d" como root, sem ninguém olhando, e o sintoma seria a VM
subir com o Docker no ar e a stack inteira parada, sem indício nenhum de que a
causa é autenticação.

Dois remédios possíveis, NESTA ORDEM:

  1) Conceder leitura do Artifact Registry à service account desta VM (rode isto
     da sua máquina, ou do Cloud Shell — não precisa ser desta VM):

       gcloud artifacts repositories add-iam-policy-binding todolist \\
         --location=us-central1 --project=sd-26-2 \\
         --member="serviceAccount:36621986996-compute@developer.gserviceaccount.com" \\
         --role="roles/artifactregistry.reader"

     Depois, rode este script de novo (ou só o pull de teste acima) para confirmar.

  2) Se MESMO DEPOIS do passo 1 o pull continuar falhando: o escopo OAuth da
     própria VM pode ser insuficiente. A service account padrão do Compute Engine
     só enxerga o Artifact Registry se a VM tiver o escopo "cloud-platform" (os
     escopos padrão — devstorage.read_only, logging.write, monitoring.write,
     service.management.readonly, servicecontrol, trace.append — NÃO incluem
     isso). Confirme os escopos atuais com:

       gcloud compute instances describe maquina-1-psd --zone=us-central1-a \\
         --format="value(serviceAccounts[0].scopes)"

     ATENÇÃO — ISTO EXIGE PARAR A VM: mudar o escopo de uma instância do Compute
     Engine só é possível com a VM DESLIGADA (gcloud compute instances stop,
     depois set-scopes, depois start). Não é algo para descobrir na véspera da
     apresentação com a stack em uso — confirme isto num ensaio, com folga, não
     no dia. Este script NÃO para nem reconfigura a VM por conta própria.

Instalação interrompida ANTES de habilitar a unit systemd — corrija a
autenticação acima e rode este script de novo.
EOF
    exit 1
fi

echo "==> Unit systemd (deploy/todolist.service)"
install -m 644 -o root -g root "$ORIGEM/todolist.service" /etc/systemd/system/todolist.service
systemctl daemon-reload
systemctl enable todolist.service

echo ""
echo "Preparação concluída. Próximos passos MANUAIS antes de subir a stack:"
echo "  1. sudo nano $DESTINO/.env   — preencha IP do Cloud SQL, senha, IMAGE_TAG"
echo "  2. sudo systemctl start todolist.service"
echo "  3. docker compose -f $DESTINO/docker-compose.prod.yml --env-file $DESTINO/.env ps"
echo ""
echo "Não abrimos porta nenhuma de firewall aqui — isso é ação do usuário no"
echo "Console/gcloud (ver deploy/README.md, seção 9)."
