#Requires -Version 7
<#
.SYNOPSIS
    Falha se algum pacote NuGet (direto ou transitivo) tem vulnerabilidade High/Critical (BE-24 CA-18).
.DESCRIPTION
    `dotnet list package --vulnerable` sempre sai com 0, então o gate é sobre o texto da saída.
#>
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)

$saida = dotnet list TodoList.sln package --vulnerable --include-transitive 2>&1 | Out-String
Write-Host $saida
if ($LASTEXITCODE -ne 0) { throw "dotnet list package falhou (exit $LASTEXITCODE)." }
if ($saida -match '(?m)^\s*>.*\s(High|Critical)\s') {
    Write-Host 'Vulnerabilidade High/Critical encontrada.' -ForegroundColor Red
    exit 1
}
Write-Host 'Nenhuma vulnerabilidade High/Critical.' -ForegroundColor Green
