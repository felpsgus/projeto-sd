#Requires -Version 7
<#
.SYNOPSIS
    Builda as quatro imagens de container (identity, tasks, gateway, frontend)
    e as empurra para o Artifact Registry, prontas para a VM (`docker compose
    -f deploy/docker-compose.prod.yml pull && up -d`) dar `pull`.

.DESCRIPTION
    Este script é o braço "imagem" da Onda D (roteiro de 22/10: uma VM só,
    tudo em Docker, Postgres no Cloud SQL). Ele NÃO SUBSTITUI
    `scripts/publish.ps1` — aquele continua servindo o caminho systemd/binário
    (plano B até 22/10, ver deploy/README.md); este aqui produz o que o
    caminho Docker (docker-compose.prod.yml) consome.

    Para cada um dos quatro serviços, roda:

        docker build --platform linux/amd64 -f <Dockerfile> -t <imagem> <contexto>
        docker push <imagem>

    Onde <imagem> é sempre
    us-central1-docker.pkg.dev/sd-26-2/todolist/todolist-<serviço>:<tag>
    (ver PARÂMETRO Tag para de onde vem <tag>).

    Contexto de build — a diferença é REAL, não descuido:

        identity/tasks/gateway  ->  contexto = raiz do repositório (o restore
                                     do .csproj precisa enxergar contracts/,
                                     Directory.Build.props e global.json, que
                                     ficam fora de src/*/*.Api/)
        frontend                ->  contexto = frontend/ (o Dockerfile do
                                     Angular não depende de nada fora dali)

    Isso está documentado no topo de cada Dockerfile — este script só
    reproduz o comando que já está escrito lá, não inventa nada novo.

    --platform linux/amd64 é passado explicitamente nos quatro builds. Hoje
    isso não muda nada (a VM é linux/amd64 e a máquina que roda este script
    também é) — o valor é blindar contra o dia em que alguém rodar isto numa
    máquina ARM (um notebook Apple Silicon, por exemplo): sem a flag, o
    Docker builda para a arquitetura da máquina local por padrão, a imagem
    sobe para o registry sem erro nenhum, e só falha NA VM, com uma mensagem
    de baixo nível sobre formato de executável que não aponta para "faltou
    --platform" de jeito nenhum.

.PARAMETER Tag
    Tag explícita para as quatro imagens. Se omitida, é derivada do commit
    atual (ver a seção "TAG" abaixo) — praticamente nunca é preciso passar
    isto à mão, exceto para reconstruir/republicar uma tag específica.

.PARAMETER DryRun
    Não builda, não tagueia, não dá push, não roda
    `gcloud auth configure-docker`. Imprime exatamente os comandos que
    SERIAM executados, na ordem em que rodariam.

    Diferença deliberada de comportamento das pré-condições sob -DryRun: elas
    continuam sendo CONSULTADAS de verdade (são leitura pura — `gcloud config
    get-value`, `gcloud services list`, `gcloud artifacts repositories
    describe` — nenhuma delas muda nada no projeto), mas uma pré-condição
    faltando vira AVISO, não interrupção. A razão é a utilidade do dry run em
    si: hoje, neste projeto (sd-26-2), a API do Artifact Registry está
    desligada e o repositório "todolist" não existe (ver deploy/README.md,
    seção do caminho Docker) — se -DryRun abortasse no primeiro obstáculo
    real, ele nunca chegaria a mostrar os quatro builds/tags/pushes
    completos, que é exatamente o que se quer ver ao ensaiar o script antes
    de as pré-condições estarem resolvidas. Fora de -DryRun, a mesma falha
    ABORTA antes de tocar em Docker — é isso que impede o script de tentar um
    `docker push` contra um repositório que não existe.

.PARAMETER SkipTests
    Pula o portão de qualidade (build + testes) antes das imagens. Existe
    para ensaios repetidos do build de imagem em si, mas o pulo é IMPRESSO em
    destaque (amarelo) — não é um caminho silencioso. Nunca use isto para
    publicar a versão que vai para a apresentação.

.EXAMPLE
    ./scripts/publish-images.ps1 -DryRun
    ./scripts/publish-images.ps1
    ./scripts/publish-images.ps1 -Tag v1-ensaio
    ./scripts/publish-images.ps1 -SkipTests -DryRun
#>
[CmdletBinding()]
param(
    [string]$Tag,
    [switch]$DryRun,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$projeto = 'sd-26-2'
$regiao = 'us-central1'
$registry = "$regiao-docker.pkg.dev/$projeto/todolist"

function Write-Etapa([string]$texto) {
    Write-Host ""
    Write-Host "==> $texto" -ForegroundColor Cyan
}

function Invoke-Verificado([string]$descricao, [scriptblock]$acao) {
    & $acao
    if ($LASTEXITCODE -ne 0) { throw "$descricao falhou (exit $LASTEXITCODE)." }
}

# ---------------------------------------------------------------------------
# TAG — o ponto mais delicado deste script.
#
# `deploy/todolist.env.example` já explica por que `latest` é perigoso num
# ambiente onde `docker compose up -d` NÃO reconsulta uma tag que já existe
# localmente: empurrar uma correção como `latest` e só reiniciar a stack na
# VM deixaria ela rodando a imagem ANTIGA, sem nenhum aviso.
#
# O padrão aqui é o SHA curto do commit (`git rev-parse --short HEAD`) — uma
# tag imutável e rastreável. MAS este repositório, agora, está com dezenas de
# arquivos não commitados (a Fase 3 inteira e as Ondas A-D do roteiro de
# 22/10). Uma imagem construída de uma árvore SUJA e tagueada com o SHA do
# último commit MENTE: ela não contém o que aquele commit contém, e duas
# pessoas (ou você, duas vezes) rodando este script em momentos diferentes,
# ambos com árvore suja, produziriam imagens DIFERENTES sob a MESMA tag — a
# pior propriedade que uma tag pode ter.
#
# Por isso: árvore suja (`git status --porcelain` não vazio) muda a tag para
# "<sha>-dirty-<carimbo>", onde o carimbo (AAAAMMDD-HHmmss) garante que dois
# builds sujos nunca colidam na mesma tag. Isso NÃO bloqueia a publicação —
# bloquear na véspera da apresentação por causa de arquivos não commitados
# seria pior do que a imprecisão da tag — mas fica escrito em amarelo,
# impossível de rolar para baixo sem ver.
function Get-TagDaImagem {
    if ($Tag) {
        Write-Host "Tag explícita: $Tag" -ForegroundColor Green
        return $Tag
    }

    Invoke-Verificado 'git rev-parse' { $null = git rev-parse --short HEAD }
    $sha = (git rev-parse --short HEAD).Trim()

    $statusPorcelain = git status --porcelain
    $arvoreSuja = -not [string]::IsNullOrWhiteSpace(($statusPorcelain | Out-String))

    if ($arvoreSuja) {
        $carimbo = Get-Date -Format 'yyyyMMdd-HHmmss'
        $tagFinal = "$sha-dirty-$carimbo"
        Write-Host ""
        Write-Host "  ================================================================" -ForegroundColor Yellow
        Write-Host "  ATENÇÃO: árvore de trabalho SUJA (arquivos não commitados)." -ForegroundColor Yellow
        Write-Host "  A imagem NÃO corresponde exatamente ao commit $sha." -ForegroundColor Yellow
        Write-Host "  Tag composta para não mentir e não colidir: $tagFinal" -ForegroundColor Yellow
        Write-Host "  ================================================================" -ForegroundColor Yellow
        Write-Host ""
        return $tagFinal
    }

    Write-Host "Árvore de trabalho limpa — tag = SHA do commit: $sha" -ForegroundColor Green
    return $sha
}

# ---------------------------------------------------------------------------
# PRÉ-CONDIÇÕES — este script CHECA, nunca CONSERTA.
#
# Habilitar uma API do GCP ou criar um repositório do Artifact Registry mexe
# na conta do usuário e pode gerar custo; a autorização para isso não foi
# dada a este script. Cada checagem abaixo é 100% leitura (`config
# get-value`, `services list`, `artifacts repositories describe`) — nenhuma
# delas cria, altera ou habilita nada — e, se faltar algo, a mensagem traz o
# comando EXATO para o usuário rodar por conta própria.
#
# Ordem deliberada: projeto/conta antes da API, API antes do repositório.
# `gcloud artifacts repositories describe` com a API desligada não devolve
# "não encontrado" — devolve PERMISSION_DENIED e, em terminal interativo,
# CHEGA A PERGUNTAR se você quer habilitar a API agora (prompt (y/N) que
# travaria um script não interativo esperando entrada que nunca vem). Por
# isso a API é verificada ANTES, com `services list` (que nunca pergunta
# nada), e o `describe` do repositório só roda depois de confirmar que a API
# já está de pé.
function Test-PreCondicoesGCP {
    $problemas = New-Object System.Collections.Generic.List[string]

    if (-not (Get-Command gcloud -ErrorAction SilentlyContinue)) {
        $problemas.Add("gcloud não encontrado no PATH. Instale o Google Cloud SDK antes de continuar.")
        return $problemas
    }

    $conta = (gcloud auth list --filter='status:ACTIVE' --format='value(account)' 2>$null | Out-String).Trim()
    if (-not $conta) {
        $problemas.Add("gcloud sem conta autenticada. Rode:`n      gcloud auth login")
    }

    $projetoAtual = (gcloud config get-value project 2>$null | Out-String).Trim()
    if ($projetoAtual -ne $projeto) {
        $problemas.Add("Projeto gcloud atual é '$projetoAtual', esperado '$projeto'. Rode:`n      gcloud config set project $projeto")
    }

    # services list --enabled: nunca pergunta nada, mesmo com a API desligada
    # — é só uma listagem. Vazio = não habilitada.
    $apiHabilitada = (gcloud services list --enabled `
            --filter='config.name:artifactregistry.googleapis.com' `
            --format='value(config.name)' --project $projeto 2>$null | Out-String).Trim()
    if (-not $apiHabilitada) {
        $problemas.Add("API artifactregistry.googleapis.com desabilitada no projeto $projeto. Rode:`n      gcloud services enable artifactregistry.googleapis.com --project $projeto`n      (a ativação leva alguns minutos para propagar)")
    }
    else {
        # Só chega aqui, e portanto só roda `describe`, se a API já estiver
        # confirmadamente habilitada — evita o prompt (y/N) descrito acima.
        gcloud artifacts repositories describe todolist --location=$regiao --project=$projeto *> $null
        if ($LASTEXITCODE -ne 0) {
            $problemas.Add("Repositório Docker 'todolist' não existe em $regiao (projeto $projeto). Rode:`n      gcloud artifacts repositories create todolist --repository-format=docker --location=$regiao --project=$projeto")
        }
    }

    return $problemas
}

Write-Etapa "Pré-condições no GCP (projeto $projeto)"
$problemas = Test-PreCondicoesGCP

if ($problemas.Count -gt 0) {
    Write-Host ""
    foreach ($p in $problemas) {
        Write-Host "  FALTANDO: $p" -ForegroundColor Red
        Write-Host ""
    }

    if ($DryRun) {
        # Ver o comentário do parâmetro -DryRun: aqui a falha vira aviso, não
        # interrupção, porque o valor do dry run é justamente mostrar o plano
        # inteiro mesmo com o ambiente ainda não pronto.
        Write-Host "  -DryRun: continuando mesmo assim, só para mostrar o plano completo." -ForegroundColor Yellow
    }
    else {
        throw "Pré-condições faltando no GCP — resolva os comandos acima antes de publicar imagens de verdade."
    }
}
else {
    Write-Host "    OK — conta, projeto, API e repositório conferidos." -ForegroundColor Green
}

$tag = Get-TagDaImagem

# ---------------------------------------------------------------------------
# PORTÃO DE QUALIDADE — mesma filosofia de scripts/publish.ps1: publicar uma
# imagem de um código que não compila, ou que passa com teste vermelho, é só
# adiar a descoberta do problema para a hora em que ele é mais caro de achar
# (a VM, ou pior, o dia da apresentação). O gate roda ANTES de qualquer
# `docker build`.
#
# E, na mesma medida, publicar não pode travar por falta de Docker Desktop
# para os testes de integração (Testcontainers) — a causa não tem nada a ver
# com a imagem em si, e a véspera da apresentação não é a hora de descobrir
# que você não consegue publicar uma correção porque o Docker Desktop
# decidiu não subir. Por isso: com Docker disponível, roda a suíte completa;
# sem ele, roda o que dá e avisa alto o que ficou de fora — nunca aborta só
# por isso.
$avisoTestesPulados = $false
$avisoDocker = $false

if ($SkipTests) {
    Write-Etapa 'Portão de qualidade — PULADO (-SkipTests)'
    Write-Host "  ATENÇÃO: -SkipTests pedido — build e testes NÃO rodaram antes de publicar" -ForegroundColor Yellow
    Write-Host "  as imagens. Nunca use isto para a versão que vai para a apresentação." -ForegroundColor Yellow
    $avisoTestesPulados = $true
}
elseif ($DryRun) {
    Write-Etapa 'Portão de qualidade — SIMULADO (-DryRun, build/testes não rodam)'
    Write-Host "  Em execução real (sem -DryRun), este passo rodaria 'dotnet build' e a" -ForegroundColor DarkGray
    Write-Host "  suíte de testes antes de qualquer 'docker build', como abaixo:" -ForegroundColor DarkGray
    Write-Host "    dotnet build -c Release --nologo -v q" -ForegroundColor DarkGray
    Write-Host "    dotnet test -c Release --no-build --nologo   (completo, se houver Docker)" -ForegroundColor DarkGray
}
else {
    Write-Etapa 'Build em Release (analyzers como erro)'
    Invoke-Verificado 'dotnet build' { dotnet build -c Release --nologo -v q }

    docker info *> $null 2>&1
    $temDocker = $LASTEXITCODE -eq 0

    if ($temDocker) {
        Write-Etapa 'Suíte de testes (completa, com Postgres via Testcontainers)'
        Invoke-Verificado 'dotnet test' { dotnet test -c Release --no-build --nologo }
    }
    else {
        Write-Etapa 'Suíte de testes (SEM Docker — integração com Postgres fica de fora)'
        Invoke-Verificado 'dotnet test' { dotnet test -c Release --no-build --nologo --filter 'Category!=Docker' }
        $avisoDocker = $true
    }
}

# ---------------------------------------------------------------------------
# Docker autenticado no registry. `gcloud auth configure-docker` só escreve
# configuração LOCAL (o helper de credenciais do seu ~/.docker/config.json)
# — não cria nem altera nada no GCP, e rodar de novo é inofensivo (idempotente
# por natureza: sobrescreve a mesma entrada de configuração). Por ser seguro
# e sem efeito colateral fora da própria máquina, este script roda sozinho em
# vez de só instruir — mas nunca sob -DryRun, para o dry run continuar sem
# tocar em nada.
Write-Etapa 'Autenticando o Docker no Artifact Registry'
if ($DryRun) {
    Write-Host "  [dry-run] gcloud auth configure-docker $regiao-docker.pkg.dev --quiet" -ForegroundColor DarkGray
}
else {
    Invoke-Verificado 'gcloud auth configure-docker' {
        gcloud auth configure-docker "$regiao-docker.pkg.dev" --quiet
    }
}

# ---------------------------------------------------------------------------
# Build + push das quatro imagens.
#
# Contexto de build: raiz do repositório para os três serviços .NET (o
# restore dos .csproj precisa enxergar contracts/, Directory.Build.props e
# global.json), frontend/ para o Angular (documentado no topo de cada
# Dockerfile — ver a seção DESCRIPTION acima).
$servicos = @(
    @{ Nome = 'identity'; Dockerfile = 'src/Identity/TodoList.Identity.Api/Dockerfile'; Contexto = '.' }
    @{ Nome = 'tasks'; Dockerfile = 'src/Tasks/TodoList.Tasks.Api/Dockerfile'; Contexto = '.' }
    @{ Nome = 'gateway'; Dockerfile = 'src/Gateway/TodoList.Gateway.Api/Dockerfile'; Contexto = '.' }
    @{ Nome = 'frontend'; Dockerfile = 'frontend/Dockerfile'; Contexto = 'frontend' }
)

$imagensPublicadas = @()

foreach ($servico in $servicos) {
    $imagem = "$registry/todolist-$($servico.Nome):$tag"

    Write-Etapa "Imagem $($servico.Nome) -> $imagem"

    $buildArgs = @('build', '--platform', 'linux/amd64', '-f', $servico.Dockerfile, '-t', $imagem, $servico.Contexto)
    $pushArgs = @('push', $imagem)

    if ($DryRun) {
        Write-Host "  [dry-run] docker $($buildArgs -join ' ')" -ForegroundColor DarkGray
        Write-Host "  [dry-run] docker $($pushArgs -join ' ')" -ForegroundColor DarkGray
    }
    else {
        Invoke-Verificado "docker build ($($servico.Nome))" { docker @buildArgs }
        Invoke-Verificado "docker push ($($servico.Nome))" { docker @pushArgs }
        Write-Host "    publicada: $imagem" -ForegroundColor Green
    }

    $imagensPublicadas += $imagem
}

Write-Host ""
if ($DryRun) {
    Write-Host 'Dry run concluído — nada foi construído, tagueado ou publicado.' -ForegroundColor Green
}
else {
    Write-Host 'Imagens publicadas.' -ForegroundColor Green
}
Write-Host ""
Write-Host "  Tag usada: $tag"
foreach ($img in $imagensPublicadas) { Write-Host "  $img" }
Write-Host ""
if ($avisoTestesPulados) {
    Write-Host '  ATENÇÃO: -SkipTests foi usado — publique com testes rodando antes da apresentação.' -ForegroundColor Yellow
}
if ($avisoDocker) {
    Write-Host '  ATENÇÃO: o Docker não estava disponível — os testes de integração com' -ForegroundColor Yellow
    Write-Host '  Postgres real NÃO rodaram. Suba o Docker e rode de novo antes da apresentação.' -ForegroundColor Yellow
}
Write-Host ''
Write-Host '  Próximo passo: atualize IMAGE_TAG no .env da VM (/opt/todolist/docker/.env)'
Write-Host "  para '$tag' e rode 'docker compose -f docker-compose.prod.yml pull && ... up -d'."
Write-Host ''
