#Requires -Version 7
<#
.SYNOPSIS
    Sobe o ambiente completo do T1 na máquina local: Postgres, migrations na ordem
    correta e os dois microsserviços, cada um na sua própria janela.

.DESCRIPTION
    Automatiza a seção "Rodando os dois serviços" do README.md — o mesmo roteiro que
    será executado na apresentação, só que contra localhost. As duas janelas separadas
    não são estética: o par de linhas de log com o mesmo traceId, uma em cada serviço,
    é a evidência de que a chamada gRPC cruzou a fronteira entre eles.

    Depois que este script terminar, dispare os quatro caminhos com:
        ./scripts/demo-curl.ps1

.PARAMETER SkipMigrations
    Pula o 'dotnet ef database update' dos dois serviços. Use em ensaios repetidos,
    quando o banco já está com o schema aplicado.

.PARAMETER PostgresPassword
    Senha do Postgres de desenvolvimento (docker-compose.yml). Só existe como
    parâmetro para não ficar fixa aqui; em nuvem ela vem de variável de ambiente.

.EXAMPLE
    ./scripts/demo-local.ps1
    ./scripts/demo-local.ps1 -SkipMigrations
#>
[CmdletBinding()]
param(
    [switch]$SkipMigrations,
    [string]$PostgresPassword = 'postgres'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$identityDb = "Host=localhost;Port=5432;Database=todolist;Username=postgres;Password=$PostgresPassword"
$tasksDb = $identityDb

function Write-Etapa([string]$texto) {
    Write-Host ""
    Write-Host "==> $texto" -ForegroundColor Cyan
}

function Wait-Endpoint([string]$url, [string]$nome, [int]$tentativas = 60) {
    for ($i = 1; $i -le $tentativas; $i++) {
        try {
            $resposta = Invoke-WebRequest -Uri $url -TimeoutSec 2 -SkipHttpErrorCheck
            if ($resposta.StatusCode -eq 200) {
                Write-Host "    $nome respondeu 200 em $url" -ForegroundColor Green
                return
            }
        }
        catch {
            # Ainda subindo: conexão recusada é o estado esperado nas primeiras
            # tentativas, não erro. Só o esgotamento das tentativas é falha.
        }
        Start-Sleep -Milliseconds 500
    }

    throw "$nome não respondeu em $url depois de $tentativas tentativas. Veja a janela do serviço."
}

Set-Location $root

Write-Etapa 'Subindo o Postgres de desenvolvimento (docker compose)'
docker compose up -d
if ($LASTEXITCODE -ne 0) { throw 'docker compose up falhou — o Docker Desktop está rodando?' }

Write-Etapa 'Esperando o Postgres aceitar conexão'
for ($i = 1; $i -le 60; $i++) {
    docker exec todolist-postgres pg_isready -U postgres -d todolist *> $null
    if ($LASTEXITCODE -eq 0) { break }
    if ($i -eq 60) { throw 'Postgres não ficou pronto a tempo.' }
    Start-Sleep -Milliseconds 500
}
Write-Host '    Postgres pronto.' -ForegroundColor Green

$env:ConnectionStrings__IdentityDb = $identityDb
$env:ConnectionStrings__TasksDb = $tasksDb

if (-not $SkipMigrations) {
    # A ordem NÃO é arbitrária: a FK cruzada tasks.tasks.owner_id -> identity.users(id)
    # faz a migration do Tasks depender da tabela criada pela do Identity (BE-02).
    Write-Etapa 'Aplicando migrations — Identity primeiro'
    dotnet ef database update --project src/Identity/TodoList.Identity.Infrastructure --startup-project src/Identity/TodoList.Identity.Api
    if ($LASTEXITCODE -ne 0) { throw 'Migration do Identity falhou.' }

    Write-Etapa 'Aplicando migrations — Tasks'
    dotnet ef database update --project src/Tasks/TodoList.Tasks.Infrastructure --startup-project src/Tasks/TodoList.Tasks.Api
    if ($LASTEXITCODE -ne 0) { throw 'Migration do Tasks falhou.' }
}

# UserStore:Provider=Persisted não é conforto: com o padrão InMemory o Identity
# aprovaria por gRPC um dono que não existe em identity.users, e a criação da tarefa
# quebraria só no INSERT, na FK cruzada — falha tardia e confusa. SeedDemoUsers popula
# os dois ids fixos usados pelo roteiro.
$comandoIdentity = @(
    "Set-Location '$root'"
    "`$env:ConnectionStrings__IdentityDb = '$identityDb'"
    "`$env:UserStore__Provider = 'Persisted'"
    "`$env:UserStore__SeedDemoUsers = 'true'"
    "`$env:ASPNETCORE_ENVIRONMENT = 'Development'"
    "`$Host.UI.RawUI.WindowTitle = 'IDENTITY (servidor gRPC) — 5080 REST / 5081 gRPC'"
    'dotnet run --project src/Identity/TodoList.Identity.Api --no-launch-profile'
) -join '; '

# AllowAnonymousCreate é o modo provisório de BE-29: a identidade vem do header
# X-User-Id porque a autenticação (BE-13/API Gateway) ainda não existe. Só para
# ambiente local e de demonstração — nunca em ambiente exposto.
$comandoTasks = @(
    "Set-Location '$root'"
    "`$env:ConnectionStrings__TasksDb = '$tasksDb'"
    "`$env:Tasks__AllowAnonymousCreate = 'true'"
    "`$env:ASPNETCORE_ENVIRONMENT = 'Development'"
    "`$Host.UI.RawUI.WindowTitle = 'TASKS (cliente gRPC) — 5100 REST'"
    'dotnet run --project src/Tasks/TodoList.Tasks.Api --no-launch-profile'
) -join '; '

Write-Etapa 'Abrindo o Identity Service em uma janela separada'
Start-Process pwsh -ArgumentList '-NoExit', '-Command', $comandoIdentity
Wait-Endpoint 'http://localhost:5080/health' 'Identity'

# O Identity precisa estar no ar ANTES do Tasks. Não é obrigatório para o Tasks
# iniciar — ele só falha na primeira criação, com 503 (D-28) —, mas subir fora de
# ordem faz o primeiro teste do ensaio dar 503 e parecer defeito.
Write-Etapa 'Abrindo o Tasks Service em uma janela separada'
Start-Process pwsh -ArgumentList '-NoExit', '-Command', $comandoTasks
Wait-Endpoint 'http://localhost:5100/health' 'Tasks'

Write-Host ""
Write-Host 'Ambiente no ar.' -ForegroundColor Green
Write-Host ''
Write-Host '  Identity  http://localhost:5080  (REST)   http://localhost:5081 (gRPC h2c)'
Write-Host '  Tasks     http://localhost:5100  (REST)'
Write-Host ''
Write-Host '  Próximo passo:  ./scripts/demo-curl.ps1'
Write-Host '  Para encerrar:  feche as duas janelas (Ctrl+C) e rode  docker compose down'
Write-Host ''
