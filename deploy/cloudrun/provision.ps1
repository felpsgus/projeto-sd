#Requires -Version 7
<#
.SYNOPSIS
    Provisiona UMA vez o que o Cloud Run precisa: APIs, Private Google Access, service accounts, secrets,
    permissões por secret e o ajuste do banco-1 (BE-45). Idempotente: o que já existe é pulado.
.PARAMETER DbUser   Usuário do banco (entra na connection string). Padrão: todolist.
.PARAMETER DryRun   Só imprime os comandos de escrita; não pergunta senha. Leituras do gcloud continuam rodando.
.NOTES
    Os secrets vêm de .secrets/jwt/*.pem (scripts/new-jwt-keys.ps1) e de Read-Host -AsSecureString.
    Nenhum valor de secret é impresso ou fica em disco depois do comando (arquivo temporário apagado).
#>
[CmdletBinding()]
param([string]$DbUser = 'todolist', [switch]$DryRun)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))

$projeto = 'sd-26-2'; $regiao = 'us-central1'; $dbHost = '10.30.240.3'; $instancia = 'banco-1'
$servicos = 'frontend', 'gateway', 'identity', 'tasks', 'migrate'
function Run([string]$que, [scriptblock]$acao) { & $acao; if ($LASTEXITCODE -ne 0) { throw "$que falhou (exit $LASTEXITCODE)." } }
# Escrita: imprime no -DryRun, executa fora dele. Todo comando que pode abrir prompt leva --quiet.
function Gcl([string[]]$a) {
    if ($DryRun) { Write-Host "[dry-run] gcloud $($a -join ' ')" } else { Run "gcloud $($a[0..2] -join ' ')" { gcloud @a } }
}
function Email([string]$s) { "todolist-$s@$projeto.iam.gserviceaccount.com" }

# Pré-condições SOMENTE LEITURA.
$faltando = @()
if (-not (gcloud auth list --filter='status:ACTIVE' --format='value(account)' 2>$null | Out-String).Trim()) { $faltando += 'gcloud auth login' }
if ((gcloud config get-value project 2>$null | Out-String).Trim() -ne $projeto) { $faltando += "gcloud config set project $projeto" }
foreach ($f in $faltando) { Write-Host "FALTANDO. Rode: $f" -ForegroundColor Red }
if ($faltando -and -not $DryRun) { throw 'Pré-condições faltando no GCP.' }
foreach ($p in 'jwt/private.pem', 'jwt/public.pem') {
    if (-not (Test-Path ".secrets/$p")) {
        Write-Host "FALTANDO .secrets/$p. Rode: ./scripts/new-jwt-keys.ps1" -ForegroundColor Red
        if (-not $DryRun) { throw "Falta .secrets/$p." }
    }
}

# 1. APIs. Com a API desligada, qualquer `describe` dela abre (y/N) e trava: só consulta o que já está ligado.
$ligadas = @(gcloud services list --enabled --project $projeto --format='value(config.name)' 2>$null)
$apis = 'run', 'secretmanager', 'iam'   # iam: criar service account falha/pergunta sem ela
$faltam = $apis | Where-Object { "$_.googleapis.com" -notin $ligadas }
if ($faltam) { Gcl (@('services', 'enable') + ($faltam | ForEach-Object { "$_.googleapis.com" }) + @('--project', $projeto, '--quiet')) }
else { Write-Host 'APIs run, secretmanager, iam: já habilitadas.' }
function Existe([string]$api, [string[]]$cmd) {
    if ($DryRun -and "$api.googleapis.com" -notin $ligadas) { return $false }   # -DryRun com a API desligada: nada existe ainda
    gcloud @cmd *> $null; return $LASTEXITCODE -eq 0
}

# 2. Private Google Access: sem ele a chamada para *.run.app não conta como interna e volta 403.
$pga = (gcloud compute networks subnets describe default --region $regiao --project $projeto --format='value(privateIpGoogleAccess)' 2>$null | Out-String).Trim()
if ($pga -eq 'True') { Write-Host 'Private Google Access: já ligado.' }
else { Gcl @('compute', 'networks', 'subnets', 'update', 'default', '--region', $regiao, '--enable-private-ip-google-access', '--project', $projeto, '--quiet') }

# 3. Service accounts (uma por serviço, só com o que usa).
foreach ($s in $servicos) {
    if (Existe 'iam' @('iam', 'service-accounts', 'describe', (Email $s), '--project', $projeto)) { Write-Host "SA todolist-${s}: já existe." }
    else { Gcl @('iam', 'service-accounts', 'create', "todolist-$s", '--display-name', "TodoList $s", '--project', $projeto, '--quiet') }
}

# 4. Secrets. Valor sempre por arquivo temporário (sem newline no fim) ou por .pem; nunca na linha de comando.
function Novo-Secret([string]$nome, [string]$arquivo) {
    if (Existe 'secretmanager' @('secrets', 'describe', $nome, '--project', $projeto)) { Write-Host "secret ${nome}: já existe."; return }
    Gcl @('secrets', 'create', $nome, '--replication-policy', 'automatic', "--data-file=$arquivo", '--project', $projeto, '--quiet')
}
Novo-Secret 'todolist-jwt-private' '.secrets/jwt/private.pem'
Novo-Secret 'todolist-jwt-public' '.secrets/jwt/public.pem'

$bancoSecrets = 'todolist-identity-db', 'todolist-tasks-db', 'todolist-db-password'
$novos = $bancoSecrets | Where-Object { -not (Existe 'secretmanager' @('secrets', 'describe', $_, '--project', $projeto)) }
$senha = $null
if ($novos -and -not $DryRun) {
    $ss = Read-Host "Senha do usuário '$DbUser' no banco (não pode ter ';' nem '=')" -AsSecureString
    $senha = [Net.NetworkCredential]::new('', $ss).Password
    if (-not $senha -or $senha -match '[;=]') { throw "Senha vazia ou com ';'/'='." }
}
$tmp = Join-Path ([IO.Path]::GetTempPath()) "todolist-secret-$PID.txt"
try {
    foreach ($n in $bancoSecrets) {
        if ($n -notin $novos) { Write-Host "secret ${n}: já existe."; continue }
        if ($DryRun) { Gcl @('secrets', 'create', $n, '--replication-policy', 'automatic', '--data-file=<valor-pedido-no-prompt>', '--project', $projeto, '--quiet'); continue }
        $valor = switch ($n) {
            'todolist-db-password' { $senha }
            default { "Host=$dbHost;Port=5432;Database=todolist;Username=$DbUser;Password=$senha;SSL Mode=Require;Maximum Pool Size=5" }
        }
        [IO.File]::WriteAllText($tmp, $valor)
        Novo-Secret $n $tmp
    }
}
finally { Remove-Item $tmp -ErrorAction SilentlyContinue; $senha = $null }

# 5. secretAccessor POR SECRET, só para a SA que o usa (a chave privada nunca chega ao gateway, e vice-versa).
$acesso = @{ 'todolist-jwt-private' = 'identity'; 'todolist-jwt-public' = 'gateway'; 'todolist-identity-db' = 'identity'
    'todolist-tasks-db' = 'tasks'; 'todolist-db-password' = 'migrate' }
foreach ($n in $acesso.Keys | Sort-Object) {
    Gcl @('secrets', 'add-iam-policy-binding', $n, '--member', "serviceAccount:$(Email $acesso[$n])", '--role', 'roles/secretmanager.secretAccessor', '--project', $projeto, '--quiet')
}

# 6. Banco: tier mínimo e sem IP público. O patch reinicia a instância e só vale com ela ligada.
$info = (gcloud sql instances describe $instancia --project $projeto --format='value(state,settings.tier,settings.ipConfiguration.ipv4Enabled)' 2>$null | Out-String).Trim() -split '\s+'
$estado, $tier, $ipPublico = $info
if ($tier -eq 'db-f1-micro' -and $ipPublico -ne 'True') { Write-Host "$instancia já é db-f1-micro sem IP público." }
else {
    Write-Host "ATENÇÃO: o patch REINICIA o $instancia (estado atual: $estado)." -ForegroundColor Yellow
    $patch = @('sql', 'instances', 'patch', $instancia, '--tier=db-f1-micro', '--no-assign-ip', '--project', $projeto, '--quiet')
    if ($estado -ne 'RUNNABLE') {
        Write-Host '  Parada: ligo junto com --activation-policy=ALWAYS. Desligue depois com demo.ps1 -Action Cooldown.' -ForegroundColor Yellow
        $patch += '--activation-policy=ALWAYS'
    }
    Gcl $patch
}
Write-Host 'Pronto. Próximo: scripts/publish-images.ps1, depois deploy/cloudrun/deploy.ps1 -Tag <tag>.' -ForegroundColor Green
