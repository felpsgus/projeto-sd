#Requires -Version 7
<#
.SYNOPSIS
    Builda e publica no Artifact Registry as quatro imagens (identity, tasks, gateway, frontend).
.PARAMETER Tag      Tag explícita. Padrão: SHA curto do commit; árvore suja vira <sha>-dirty-<AAAAMMDD-HHmmss>.
.PARAMETER DryRun   Só imprime os comandos; pré-condições faltando viram aviso em vez de erro.
.PARAMETER SkipTests Pula build+testes antes das imagens (nunca para a versão da apresentação).
#>
[CmdletBinding()]
param([string]$Tag, [switch]$DryRun, [switch]$SkipTests)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)

$projeto = 'sd-26-2'; $regiao = 'us-central1'; $registry = "$regiao-docker.pkg.dev/$projeto/todolist"
function Run([string]$que, [scriptblock]$acao) { & $acao; if ($LASTEXITCODE -ne 0) { throw "$que falhou (exit $LASTEXITCODE)." } }

# Pré-condições SOMENTE LEITURA: este script nunca habilita API nem cria recurso (mexe na conta e gera custo).
# A API é checada antes do repositório: `repositories describe` com a API desligada pergunta (y/N) e trava.
$faltando = @()
if (-not (gcloud auth list --filter='status:ACTIVE' --format='value(account)' 2>$null | Out-String).Trim()) { $faltando += 'gcloud auth login' }
if ((gcloud config get-value project 2>$null | Out-String).Trim() -ne $projeto) { $faltando += "gcloud config set project $projeto" }
if (-not (gcloud services list --enabled --filter='config.name:artifactregistry.googleapis.com' --format='value(config.name)' --project $projeto 2>$null | Out-String).Trim()) {
    $faltando += "gcloud services enable artifactregistry.googleapis.com --project $projeto"
} else {
    gcloud artifacts repositories describe todolist --location=$regiao --project=$projeto *> $null
    if ($LASTEXITCODE -ne 0) { $faltando += "gcloud artifacts repositories create todolist --repository-format=docker --location=$regiao --project=$projeto" }
}
foreach ($f in $faltando) { Write-Host "FALTANDO. Rode: $f" -ForegroundColor Red }
if ($faltando -and -not $DryRun) { throw 'Pré-condições faltando no GCP.' }

# Tag imutável: a árvore suja não pode reusar o SHA (imagens diferentes sob a mesma tag).
if (-not $Tag) {
    $Tag = (git rev-parse --short HEAD).Trim()
    if (git status --porcelain) {
        $Tag = "$Tag-dirty-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
        Write-Host "ATENÇÃO: árvore suja; a imagem não é exatamente o commit. Tag: $Tag" -ForegroundColor Yellow
    }
}

if ($SkipTests) { Write-Host 'ATENÇÃO: -SkipTests, build e testes NÃO rodaram.' -ForegroundColor Yellow }
elseif (-not $DryRun) {
    Run 'dotnet build' { dotnet build -c Release --nologo -v q }
    docker info *> $null 2>&1
    $filtro = $LASTEXITCODE -eq 0 ? @() : @('--filter', 'Category!=Docker')   # sem Docker, sem Testcontainers
    Run 'dotnet test' { dotnet test -c Release --no-build --nologo @filtro }
}

if (-not $DryRun) { Run 'configure-docker' { gcloud auth configure-docker "$regiao-docker.pkg.dev" --quiet } }

# identity/tasks/gateway: contexto = raiz (o restore precisa de contracts/, Directory.Build.props, global.json).
# frontend: contexto = frontend/. --platform explícito: sem ele, um notebook ARM publica imagem que só falha na VM.
$servicos = @(
    @('identity', 'src/Identity/TodoList.Identity.Api/Dockerfile', '.'), @('tasks', 'src/Tasks/TodoList.Tasks.Api/Dockerfile', '.'),
    @('gateway', 'src/Gateway/TodoList.Gateway.Api/Dockerfile', '.'), @('frontend', 'frontend/Dockerfile', 'frontend'))
foreach ($s in $servicos) {
    $nome, $dockerfile, $ctx = $s; $img = "$registry/todolist-${nome}:$Tag"
    $cmds = @(@('build', '--platform', 'linux/amd64', '-f', $dockerfile, '-t', $img, $ctx), @('push', $img))
    foreach ($c in $cmds) { if ($DryRun) { Write-Host "[dry-run] docker $($c -join ' ')" } else { Run "docker $($c[0]) $nome" { docker @c } } }
}
Write-Host "Tag: $Tag. Atualize IMAGE_TAG no .env da VM." -ForegroundColor Green
