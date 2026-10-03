#Requires -Version 7
<#
.SYNOPSIS
    Gate de cobertura do backend (BE-24 CA-14): falha se algum piso não for atingido.
.DESCRIPTION
    Lê o JsonSummary do ReportGenerator. Falha (exit 1) se a cobertura de linhas global
    for menor que -MinLine, ou se a agregada dos assemblies *.Domain + *.Application
    for menor que -MinDomainApplication. Sempre imprime os números por assembly.
#>
[CmdletBinding()]
param(
    [string]$Summary = (Join-Path (Split-Path -Parent $PSScriptRoot) 'coverage-report/Summary.json'),
    [double]$MinLine = 75,
    [double]$MinDomainApplication = 85
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path $Summary)) { throw "Resumo não encontrado: $Summary (rode ./scripts/coverage.ps1)." }

$json = Get-Content $Summary -Raw | ConvertFrom-Json
$assemblies = @($json.coverage.assemblies)
$pct = { param($c, $t) if ($t -eq 0) { 100.0 } else { [math]::Round(100.0 * $c / $t, 2) } }

foreach ($a in $assemblies | Sort-Object name) {
    '{0,-50} {1,6}%  ({2}/{3} linhas)' -f $a.name, (& $pct $a.coveredlines $a.coverablelines), $a.coveredlines, $a.coverablelines
}

$global = [double]$json.summary.linecoverage
$da = @($assemblies | Where-Object { $_.name -match '\.(Domain|Application)$' })
$daCovered = ($da | Measure-Object coveredlines -Sum).Sum
$daTotal = ($da | Measure-Object coverablelines -Sum).Sum
$daPct = & $pct $daCovered $daTotal

''
'Global (linhas):          {0}%  (piso {1}%)' -f $global, $MinLine
'Domain + Application:     {0}%  (piso {1}%)  [{2}/{3} linhas]' -f $daPct, $MinDomainApplication, $daCovered, $daTotal

$falhas = @()
if ($global -lt $MinLine) { $falhas += "cobertura global $global% < $MinLine%" }
if ($daPct -lt $MinDomainApplication) { $falhas += "Domain+Application $daPct% < $MinDomainApplication%" }
if ($falhas) { Write-Host "GATE FALHOU: $($falhas -join '; ')" -ForegroundColor Red; exit 1 }
Write-Host 'Gate de cobertura OK.' -ForegroundColor Green
