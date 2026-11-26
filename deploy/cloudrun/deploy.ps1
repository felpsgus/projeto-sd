#Requires -Version 7
<#
.SYNOPSIS
    Publica no Cloud Run o job de migração e os 4 serviços, nesta ordem: migrate, identity, tasks, gateway, frontend (BE-45).
.PARAMETER Tag       Tag da imagem publicada por scripts/publish-images.ps1.
.PARAMETER Exposure  Internal (padrão): identity e tasks com --ingress internal. Public: --ingress all (se o professor pedir).
                     Rodar de novo com o outro valor é a troca; nada a editar.
.PARAMETER DbUser    Usuário do banco para o job de migração. Padrão: todolist.
.PARAMETER DryRun    Só imprime os comandos de escrita. URLs ainda inexistentes viram <url-de-svc>.
#>
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Tag, [ValidateSet('Internal', 'Public')][string]$Exposure = 'Internal',
    [string]$DbUser = 'todolist', [switch]$DryRun)
$ErrorActionPreference = 'Stop'

$projeto = 'sd-26-2'; $regiao = 'us-central1'; $registry = "$regiao-docker.pkg.dev/$projeto/todolist"; $dbHost = '10.30.240.3'
function Run([string]$que, [scriptblock]$acao) { & $acao; if ($LASTEXITCODE -ne 0) { throw "$que falhou (exit $LASTEXITCODE)." } }
function Gcl([string[]]$a) {
    if ($DryRun) { Write-Host "[dry-run] gcloud $($a -join ' ')" } else { Run "gcloud $($a[0..2] -join ' ')" { gcloud @a } }
}
function Sa([string]$s) { "todolist-$s@$projeto.iam.gserviceaccount.com" }
function Img([string]$s) { "$registry/todolist-${s}:$Tag" }

# Pré-condições SOMENTE LEITURA. `run services describe` com a API desligada abre (y/N) e trava: confere a API antes.
$faltando = @()
if (-not (gcloud auth list --filter='status:ACTIVE' --format='value(account)' 2>$null | Out-String).Trim()) { $faltando += 'gcloud auth login' }
if ((gcloud config get-value project 2>$null | Out-String).Trim() -ne $projeto) { $faltando += "gcloud config set project $projeto" }
$runLigada = [bool](gcloud services list --enabled --filter='config.name:run.googleapis.com' --format='value(config.name)' --project $projeto 2>$null | Out-String).Trim()
if (-not $runLigada) { $faltando += 'deploy/cloudrun/provision.ps1 (habilita a API run)' }
foreach ($f in $faltando) { Write-Host "FALTANDO. Rode: $f" -ForegroundColor Red }
if ($faltando -and -not $DryRun) { throw 'Pré-condições faltando no GCP.' }

function Url([string]$s) {
    $u = if ($runLigada) { (gcloud run services describe "todolist-$s" --region $regiao --project $projeto --format 'value(status.url)' 2>$null | Out-String).Trim() }
    if ($u) { return $u }
    if ($DryRun) { return "<url-de-$s>" }
    throw "Sem URL para todolist-${s}: o deploy anterior falhou?"
}

$ingress = $Exposure -eq 'Internal' ? 'internal' : 'all'
$vpc = @('--network', 'default', '--subnet', 'default')
$comuns = @('--region', $regiao, '--project', $projeto, '--quiet')
$grpc = @('--use-http2', '--port', '8080', '--allow-unauthenticated', '--max-instances', '2', '--cpu', '1', '--memory', '512Mi')
# `--set-env-vars` e `--set-secrets` substituem o conjunto inteiro: o deploy é declarativo e repetível.
function Env([string[]]$pares) { '--set-env-vars', ($pares -join ',') }

# 1. Job de migração (SA própria, só a senha) e execução. O SQL é idempotente: rodar contra banco populado é seguro.
Gcl (@('run', 'jobs', 'deploy', 'todolist-migrate', '--image', (Img 'migrate'), '--service-account', (Sa 'migrate')) + $vpc + @('--vpc-egress', 'private-ranges-only') +
    (Env "PGHOST=$dbHost", 'PGPORT=5432', 'PGDATABASE=todolist', "PGUSER=$DbUser", 'PGSSLMODE=require') +
    @('--set-secrets', 'PGPASSWORD=todolist-db-password:latest', '--max-retries', '0') + $comuns)
Gcl (@('run', 'jobs', 'execute', 'todolist-migrate', '--wait') + $comuns)

# 2. identity: chave PRIVADA (volume) + connection string. Ingress interno por padrão; sem IAM entre serviços (D-45).
Gcl (@('run', 'deploy', 'todolist-identity', '--image', (Img 'identity'), '--service-account', (Sa 'identity'), '--ingress', $ingress) + $grpc + $vpc + @('--vpc-egress', 'private-ranges-only') +
    (Env 'ASPNETCORE_ENVIRONMENT=Production', 'UserStore__Provider=Persisted', 'Jwt__PrivateKeyPath=/secrets/jwt/private.pem') +
    @('--set-secrets', 'ConnectionStrings__IdentityDb=todolist-identity-db:latest,/secrets/jwt/private.pem=todolist-jwt-private:latest') + $comuns)
$identity = Url 'identity'

# 3. tasks: all-traffic porque chama o identity pela URL run.app (sai pela VPC com Private Google Access).
Gcl (@('run', 'deploy', 'todolist-tasks', '--image', (Img 'tasks'), '--service-account', (Sa 'tasks'), '--ingress', $ingress) + $grpc + $vpc + @('--vpc-egress', 'all-traffic') +
    (Env 'ASPNETCORE_ENVIRONMENT=Production', "Identity__GrpcAddress=$identity") +
    @('--set-secrets', 'ConnectionStrings__TasksDb=todolist-tasks-db:latest') + $comuns)
$tasks = Url 'tasks'

# 4. gateway: borda pública, HTTP/1, só a chave PÚBLICA.
Gcl (@('run', 'deploy', 'todolist-gateway', '--image', (Img 'gateway'), '--service-account', (Sa 'gateway'), '--ingress', 'all', '--allow-unauthenticated', '--port', '8080',
        '--max-instances', '2', '--cpu', '1', '--memory', '512Mi') + $vpc + @('--vpc-egress', 'all-traffic') +
    (Env 'ASPNETCORE_ENVIRONMENT=Production', "Backends__IdentityGrpcAddress=$identity", "Backends__TasksGrpcAddress=$tasks", 'Backends__IdentityGrpcTimeoutSeconds=10', 'Jwt__PublicKeyPath=/secrets/jwt/public.pem') +
    @('--set-secrets', '/secrets/jwt/public.pem=todolist-jwt-public:latest') + $comuns)
$gateway = Url 'gateway'

# 5. frontend: nginx com proxy /api para o gateway (URL com esquema, sem barra no fim).
Gcl (@('run', 'deploy', 'todolist-frontend', '--image', (Img 'frontend'), '--service-account', (Sa 'frontend'), '--ingress', 'all', '--allow-unauthenticated', '--port', '8080', '--max-instances', '2') +
    (Env "GATEWAY_UPSTREAM=$gateway") + $comuns)
$frontend = Url 'frontend'

Write-Host "`nURLs (exposição: $Exposure)" -ForegroundColor Green
@([pscustomobject]@{ Serviço = 'frontend'; Acesso = 'pública'; Url = $frontend }
    [pscustomobject]@{ Serviço = 'gateway'; Acesso = 'pública'; Url = $gateway }
    [pscustomobject]@{ Serviço = 'identity'; Acesso = $Exposure -eq 'Internal' ? 'interna' : 'pública'; Url = $identity }
    [pscustomobject]@{ Serviço = 'tasks'; Acesso = $Exposure -eq 'Internal' ? 'interna' : 'pública'; Url = $tasks }) | Format-Table -AutoSize
Write-Host "Próximo: DEMO_PASSWORD=... bash deploy/cloudrun/smoke.sh $frontend"
