#Requires -Version 7
<#
.SYNOPSIS
    Sobe o ambiente completo do T2 na máquina local: Postgres, migrations na ordem
    correta e os três microsserviços (Identity, Tasks, Gateway), cada um na sua
    própria janela.

.DESCRIPTION
    Automatiza a seção "Rodando o T2" do README.md — o mesmo roteiro que será
    executado na apresentação, só que contra localhost. As três janelas separadas
    não são estética: o trio de linhas de log com o mesmo traceId, uma em cada
    serviço, é a evidência de que a requisição atravessou Gateway -> Tasks ->
    Identity (e Gateway -> Identity, na validação do token) por gRPC.

    Depois que este script terminar, dispare os seis passos com:
        ./scripts/demo-t2.ps1 -DemoPassword <a senha impressa abaixo>

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

# Jwt:SigningKey (BE-08) NUNCA é versionada — nem aqui. Uma chave aleatória de
# 48 bytes por execução, só para esta demonstração local; passada ao processo
# do Identity por variável de ambiente (Jwt__SigningKey), nunca gravada em
# appsettings*.json. O Tasks Service não recebe esta variável (D-31, CA-14 de
# BE-08): ele não valida JWT.
$jwtSigningKey = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))

# UserStore:DemoUserPassword (BE-33) também NUNCA é versionada. Senha aleatória
# por execução, impressa no console para o roteiro de login — o seed regrava o
# hash dos dois usuários de demonstração toda vez que ela mudar (idempotente).
$demoUserPassword = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(12))

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
    "`$env:UserStore__DemoUserPassword = '$demoUserPassword'"
    "`$env:Jwt__SigningKey = '$jwtSigningKey'"
    "`$env:ASPNETCORE_ENVIRONMENT = 'Development'"
    "`$Host.UI.RawUI.WindowTitle = 'IDENTITY (servidor gRPC) — 5080 REST / 5081 gRPC'"
    'dotnet run --project src/Identity/TodoList.Identity.Api --no-launch-profile'
) -join '; '

# O Tasks não tem mais gatilho REST próprio (BE-35): só é alcançável por gRPC,
# pelo Gateway. A identidade do dono da tarefa chega pela metadata gRPC
# x-user-id, preenchida pelo Gateway depois de validar o token (D-34) — o
# Tasks não recebe, e não precisa de, nenhuma variável de autenticação.
$comandoTasks = @(
    "Set-Location '$root'"
    "`$env:ConnectionStrings__TasksDb = '$tasksDb'"
    "`$env:ASPNETCORE_ENVIRONMENT = 'Development'"
    "`$Host.UI.RawUI.WindowTitle = 'TASKS (servidor gRPC) — 5101 gRPC'"
    'dotnet run --project src/Tasks/TodoList.Tasks.Api --no-launch-profile'
) -join '; '

# O Gateway é a única borda REST (D-32): nenhuma variável de conexão com banco,
# nenhuma chave Jwt:* — ele nunca valida token localmente, só pergunta ao
# Identity via ValidateToken (D-31). Os endereços gRPC default de
# appsettings.Development.json (localhost:5081/5101) já apontam para os dois
# processos acima, então nenhuma variável de Backends:* é necessária aqui.
$comandoGateway = @(
    "Set-Location '$root'"
    "`$env:ASPNETCORE_ENVIRONMENT = 'Development'"
    "`$Host.UI.RawUI.WindowTitle = 'GATEWAY (borda REST) — 8080 HTTP'"
    'dotnet run --project src/Gateway/TodoList.Gateway.Api --no-launch-profile'
) -join '; '

Write-Etapa 'Abrindo o Identity Service em uma janela separada'
Start-Process pwsh -ArgumentList '-NoExit', '-Command', $comandoIdentity
Wait-Endpoint 'http://localhost:5080/health' 'Identity'

# O Identity precisa estar no ar ANTES do Tasks e do Gateway. Não é obrigatório
# para o Tasks/Gateway iniciarem — eles só falham na primeira chamada, com 503
# (D-28) —, mas subir fora de ordem faz o primeiro teste do ensaio dar 503 e
# parecer defeito.
Write-Etapa 'Abrindo o Tasks Service em uma janela separada'
Start-Process pwsh -ArgumentList '-NoExit', '-Command', $comandoTasks
Wait-Endpoint 'http://localhost:5100/health' 'Tasks'

Write-Etapa 'Abrindo o API Gateway em uma janela separada'
Start-Process pwsh -ArgumentList '-NoExit', '-Command', $comandoGateway
Wait-Endpoint 'http://localhost:8080/health' 'Gateway'

Write-Host ""
Write-Host 'Ambiente no ar.' -ForegroundColor Green
Write-Host ''
Write-Host '  Identity  http://localhost:5080  (REST /health)   http://localhost:5081 (gRPC h2c)'
Write-Host '  Tasks     http://localhost:5100  (REST /health)   http://localhost:5101 (gRPC h2c)'
Write-Host '  Gateway   http://localhost:8080  (REST — a única borda pública, D-32)'
Write-Host ''
Write-Host "  Senha dos usuários de demonstração (Login, BE-33): $demoUserPassword" -ForegroundColor Yellow
Write-Host ''
Write-Host "  Próximo passo:  ./scripts/demo-t2.ps1 -DemoPassword $demoUserPassword"
Write-Host '  Para encerrar:  feche as três janelas (Ctrl+C) e rode  docker compose down'
Write-Host ''
