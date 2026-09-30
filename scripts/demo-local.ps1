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

    Depois que este script terminar, dispare os passos com:
        ./scripts/demo-t2.ps1 -Password <qualquer senha de desenvolvimento, 8+ caracteres, letra e número>

    Onda E (T2): não existe mais seed de demonstração — scripts/demo-t2.ps1
    cadastra sua própria conta a cada execução (POST /api/auth/register), então
    a senha aqui não precisa mais ser sincronizada entre este script e aquele:
    qualquer valor que satisfaça a política de senha (RN-AUTH-04) serve.

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

# JWT RS256 (BE-40, D-38): o Identity assina com a chave privada, o Gateway
# valida localmente com a pública — nenhuma chave sai daqui como variável
# aleatória por execução, porque a mesma chave precisa existir nos dois
# processos. Em vez disso, gera (uma vez) um par RSA 2048 persistido em
# .secrets/jwt/ (ignorado pelo git, ver .gitignore) e reaproveita nas execuções
# seguintes. O Tasks Service não recebe nenhuma das duas variáveis abaixo (D-38,
# CA-18 de BE-40): ele não sabe nada sobre tokens.
$jwtKeysDir = Join-Path $root '.secrets/jwt'
$jwtPrivateKeyPath = Join-Path $jwtKeysDir 'private.pem'
$jwtPublicKeyPath = Join-Path $jwtKeysDir 'public.pem'

if (-not (Test-Path $jwtPrivateKeyPath) -or -not (Test-Path $jwtPublicKeyPath)) {
    Write-Etapa 'Gerando o par de chaves JWT RS256 (primeira execução, .secrets/jwt/)'
    & (Join-Path $PSScriptRoot 'new-jwt-keys.ps1') -OutDir $jwtKeysDir
}
else {
    Write-Host "    Reaproveitando par de chaves JWT existente em $jwtKeysDir" -ForegroundColor DarkGray
}

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
# quebraria só no INSERT, na FK cruzada — falha tardia e confusa. Onda E: não há
# mais seed nenhum para popular contas — cadastre pela tela ou por
# scripts/demo-t2.ps1 (que se cadastra sozinho a cada execução).
$comandoIdentity = @(
    "Set-Location '$root'"
    "`$env:ConnectionStrings__IdentityDb = '$identityDb'"
    "`$env:UserStore__Provider = 'Persisted'"
    "`$env:Jwt__PrivateKeyPath = '$jwtPrivateKeyPath'"
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

# O Gateway é a única borda REST (D-32) e, desde BE-40 (D-38), valida o JWT
# localmente com AddJwtBearer e a chave PÚBLICA — não pergunta mais ao Identity
# via ValidateToken a cada requisição. Nenhuma variável de conexão com banco;
# Jwt:Issuer/Jwt:Audience já vêm de appsettings.json, só Jwt:PublicKeyPath
# precisa vir daqui. Os endereços gRPC default de appsettings.Development.json
# (localhost:5081/5101) já apontam para os dois processos acima, então nenhuma
# variável de Backends:* é necessária aqui.
$comandoGateway = @(
    "Set-Location '$root'"
    "`$env:Jwt__PublicKeyPath = '$jwtPublicKeyPath'"
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
Write-Host '  Próximo passo:  ./scripts/demo-t2.ps1 -Password "<sua senha de desenvolvimento>"'
Write-Host '                  (scripts/demo-t2.ps1 cadastra sua própria conta a cada execução — Onda E)'
Write-Host '  Para encerrar:  feche as três janelas (Ctrl+C) e rode  docker compose down'
Write-Host ''
