#Requires -Version 7
<#
.SYNOPSIS
    Comando único de cobertura do backend: testes com coleta -> relatório -> gate.
.DESCRIPTION
    Saída: coverage-report/ (index.html, SummaryGithub.md, Summary.json). Pisos e exclusões:
    README (seção "CI e gates") e coverlet.runsettings. -NoBuild reaproveita um build prévio (CI).
.EXAMPLE
    ./scripts/coverage.ps1
    ./scripts/coverage.ps1 -Filter "Category!=Docker"
#>
[CmdletBinding()]
param(
    [string]$Filter,
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

Remove-Item (Join-Path $root 'coverage-results'), (Join-Path $root 'coverage-report') -Recurse -Force -ErrorAction SilentlyContinue

dotnet tool restore
if ($LASTEXITCODE -ne 0) { throw 'dotnet tool restore falhou.' }

$testArgs = @('test', '--settings', 'coverlet.runsettings', '--collect:XPlat Code Coverage', '--results-directory', 'coverage-results')
if ($NoBuild) { $testArgs += '--no-build' }
if ($Filter) { $testArgs += @('--filter', $Filter) }
dotnet @testArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet test falhou (exit $LASTEXITCODE)." }

dotnet tool run reportgenerator '-reports:coverage-results/**/coverage.cobertura.xml' '-targetdir:coverage-report' '-reporttypes:Html;MarkdownSummaryGithub;JsonSummary'
if ($LASTEXITCODE -ne 0) { throw "reportgenerator falhou (exit $LASTEXITCODE)." }

& (Join-Path $PSScriptRoot 'check-coverage.ps1')
exit $LASTEXITCODE
