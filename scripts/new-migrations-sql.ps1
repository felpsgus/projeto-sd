#Requires -Version 7
# Gera artifacts/sql/01-identity.sql e 02-tasks.sql (idempotentes), aplicados pelo
# serviço `migrate` dos dois compose files. Ordem 01 antes de 02: a FK cruzada
# tasks.tasks.owner_id -> identity.users(id) faz o Tasks depender do Identity.
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$sqlRoot = Join-Path $root 'artifacts/sql'
New-Item -ItemType Directory -Path $sqlRoot -Force | Out-Null

$migrations = @(
    @{ Arquivo = '01-identity.sql'; Projeto = 'src/Identity/TodoList.Identity.Infrastructure'; Startup = 'src/Identity/TodoList.Identity.Api' }
    @{ Arquivo = '02-tasks.sql';    Projeto = 'src/Tasks/TodoList.Tasks.Infrastructure';       Startup = 'src/Tasks/TodoList.Tasks.Api' }
)

Set-Location $root

foreach ($migration in $migrations) {
    $saida = Join-Path $sqlRoot $migration.Arquivo

    Write-Host ""
    Write-Host "==> Gerando $($migration.Arquivo) (idempotente)" -ForegroundColor Cyan

    # Sem --no-build o `dotnet ef` compila o startup project sozinho. Em Debug,
    # se os serviços estiverem rodando localmente, o executável em bin/Debug está
    # travado pelo processo e o build falha com MSB3027 — um erro que não tem
    # nada a ver com migration nenhuma. Por isso o build é explícito, em Release.
    dotnet build $migration.Startup -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "build de $($migration.Startup) falhou (exit $LASTEXITCODE)." }

    dotnet ef migrations script --idempotent `
        --project $migration.Projeto --startup-project $migration.Startup `
        --configuration Release --no-build `
        --output $saida
    if ($LASTEXITCODE -ne 0) { throw "script de migration $($migration.Arquivo) falhou (exit $LASTEXITCODE)." }

    Write-Host "    $saida" -ForegroundColor Green
}
