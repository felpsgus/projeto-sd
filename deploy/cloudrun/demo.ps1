#Requires -Version 7
<#
.SYNOPSIS
    Liga/desliga o ambiente da demo (BE-45).
.PARAMETER Action
    Warmup    banco ligado + --min 1 nos 4 serviços (nível do serviço, sem criar revisão) (rode ~30 min antes).
    Cooldown  o inverso: min-instances 0 e banco parado (rode logo depois, para não gastar crédito).
    TasksDown desliga o Tasks sem apagá-lo (--scaling=0): a cena da queda.
    TasksUp   volta ao autoscaling.
.PARAMETER DryRun  Só imprime os comandos.
#>
[CmdletBinding()]
param([Parameter(Mandatory)][ValidateSet('Warmup', 'Cooldown', 'TasksDown', 'TasksUp')][string]$Action, [switch]$DryRun)
$ErrorActionPreference = 'Stop'

$projeto = 'sd-26-2'; $regiao = 'us-central1'
function Run([string]$que, [scriptblock]$acao) { & $acao; if ($LASTEXITCODE -ne 0) { throw "$que falhou (exit $LASTEXITCODE)." } }
function Gcl([string[]]$a) {
    if ($DryRun) { Write-Host "[dry-run] gcloud $($a -join ' ')" } else { Run "gcloud $($a[0..2] -join ' ')" { gcloud @a } }
}
function Banco([string]$politica) { Gcl @('sql', 'instances', 'patch', 'banco-1', "--activation-policy=$politica", '--project', $projeto, '--quiet') }
function Servicos([string]$valor) {
    foreach ($s in 'identity', 'tasks', 'gateway', 'frontend') {
        Gcl @('run', 'services', 'update', "todolist-$s", '--min', $valor, '--region', $regiao, '--project', $projeto, '--quiet')
    }
}

# Só leitura: `run services update` com a API desligada abre (y/N) e trava.
if (-not $DryRun -and -not (gcloud services list --enabled --filter='config.name:run.googleapis.com' --format='value(config.name)' --project $projeto 2>$null | Out-String).Trim()) {
    throw 'API run desligada: rode deploy/cloudrun/provision.ps1 e deploy.ps1 antes.'
}

switch ($Action) {
    'Warmup' { Banco 'ALWAYS'; Servicos '1'; Write-Host 'Pronto. Rode o smoke.sh e espere os 4 serviços ficarem verdes.' -ForegroundColor Green }
    'Cooldown' { Servicos '0'; Banco 'NEVER'; Write-Host 'Pronto. Banco parado, serviços escalam a zero.' -ForegroundColor Green }
    'TasksDown' { Gcl @('run', 'services', 'update', 'todolist-tasks', '--scaling=0', '--region', $regiao, '--project', $projeto, '--quiet') }
    'TasksUp' { Gcl @('run', 'services', 'update', 'todolist-tasks', '--scaling=auto', '--region', $regiao, '--project', $projeto, '--quiet') }
}
