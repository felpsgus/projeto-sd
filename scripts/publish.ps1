#Requires -Version 7
<#
.SYNOPSIS
    Empacota os dois serviços para Linux x64 e gera os scripts SQL das migrations,
    prontos para subir na VM do GCP.

.DESCRIPTION
    Produz, em artifacts/:

        publish/identity/       binário do Identity Service (linux-x64)
        publish/tasks/          binário do Tasks Service (linux-x64)
        publish/gateway/        binário do API Gateway (linux-x64)
        sql/01-identity.sql     script idempotente das migrations do Identity
        sql/02-tasks.sql        script idempotente das migrations do Tasks
        todolist-deploy.tar.gz  tudo acima + os arquivos de deploy, num arquivo só

    Três decisões deliberadas:

    1. **Framework-dependent por padrão, self-contained sob demanda.** O padrão
       gera ~20 MB por serviço e exige o runtime ASP.NET Core instalado na VM (um
       `apt install`, uma vez). Isso existe porque o upload aqui é pelo SSH do
       navegador, onde 280 MB de pacote self-contained é sofrimento. Use
       -SelfContained quando houver `scp` de verdade: aí o runtime viaja junto e a
       VM não precisa de nada instalado.

       Nos dois modos o executável tem o MESMO nome e caminho
       (TodoList.Identity.Api, sem extensão), porque `-r linux-x64` gera o apphost
       nativo mesmo sem self-contained — então os units do systemd não mudam entre
       um modo e outro.

    2. **Migrations como SQL idempotente, não `dotnet ef` na VM.** O `dotnet ef` é
       ferramenta de SDK, e instalar o SDK na VM só para migrar é peso
       desnecessário. O script idempotente pode ser aplicado com `psql`, roda
       quantas vezes for preciso sem quebrar, e é um artefato que dá para ler antes
       de executar.

    3. **Um .tar.gz no fim.** O SSH do navegador do GCP sobe um arquivo por vez e
       não sobe pastas. Um tarball único é a diferença entre um upload e algumas
       dezenas.

    O número dos arquivos SQL (01, 02) não é enfeite: a FK cruzada
    tasks.tasks.owner_id -> identity.users(id) faz o script do Tasks depender da
    tabela criada pelo do Identity. Aplicar fora de ordem falha.

.PARAMETER OutputRoot
    Diretório de saída. Padrão: artifacts/ na raiz do repositório (gitignored).

.PARAMETER SelfContained
    Empacota o runtime .NET junto (~140 MB por serviço). Dispensa instalar runtime
    na VM, mas inviabiliza o upload pelo navegador.

.PARAMETER SkipSql
    Não regenera os scripts SQL das migrations.

.EXAMPLE
    ./scripts/publish.ps1
    ./scripts/publish.ps1 -SelfContained
#>
[CmdletBinding()]
param(
    [string]$OutputRoot,
    [switch]$SelfContained,
    [switch]$SkipSql
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
if (-not $OutputRoot) { $OutputRoot = Join-Path $root 'artifacts' }

$runtime = 'linux-x64'

function Write-Etapa([string]$texto) {
    Write-Host ""
    Write-Host "==> $texto" -ForegroundColor Cyan
}

function Invoke-Verificado([string]$descricao, [scriptblock]$acao) {
    & $acao
    if ($LASTEXITCODE -ne 0) { throw "$descricao falhou (exit $LASTEXITCODE)." }
}

Set-Location $root

# Publicar um build que não compila, ou que compila com teste vermelho, é como se
# descobre na VM que o problema veio de casa. O portão fica aqui, antes do upload.
Write-Etapa 'Build em Release (analyzers como erro)'
Invoke-Verificado 'dotnet build' { dotnet build -c Release --nologo -v q }

# Os testes de integração sobem Postgres via Testcontainers. Sem Docker eles não
# rodam — e travar a publicação por isso seria errado: a causa não tem relação
# nenhuma com o pacote, e na véspera da apresentação o efeito seria você não
# conseguir publicar uma correção porque o Docker Desktop resolveu não subir.
# Rodamos o que dá, e dizemos alto e claro o que ficou de fora.
docker info *> $null 2>&1
$temDocker = $LASTEXITCODE -eq 0

if ($temDocker) {
    Write-Etapa 'Suíte de testes (completa, com Postgres via Testcontainers)'
    Invoke-Verificado 'dotnet test' { dotnet test -c Release --no-build --nologo }
}
else {
    Write-Etapa 'Suíte de testes (SEM Docker — integração com Postgres fica de fora)'
    Invoke-Verificado 'dotnet test' { dotnet test -c Release --no-build --nologo --filter 'Category!=Docker' }
    $avisoDocker = $true
}

$publishRoot = Join-Path $OutputRoot 'publish'
$sqlRoot = Join-Path $OutputRoot 'sql'

if (Test-Path $publishRoot) { Remove-Item $publishRoot -Recurse -Force }
New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null
New-Item -ItemType Directory -Path $sqlRoot -Force | Out-Null

$servicos = @(
    @{ Nome = 'identity'; Projeto = 'src/Identity/TodoList.Identity.Api' }
    @{ Nome = 'tasks';    Projeto = 'src/Tasks/TodoList.Tasks.Api' }
    @{ Nome = 'gateway';  Projeto = 'src/Gateway/TodoList.Gateway.Api' }
)

$selfContainedFlag = if ($SelfContained) { 'true' } else { 'false' }
$modo = if ($SelfContained) { 'self-contained' } else { 'framework-dependent — exige runtime ASP.NET Core na VM' }

foreach ($servico in $servicos) {
    $destino = Join-Path $publishRoot $servico.Nome

    Write-Etapa "Publicando $($servico.Nome) para $runtime ($modo)"
    Invoke-Verificado "publish do $($servico.Nome)" {
        dotnet publish $servico.Projeto -c Release -r $runtime --self-contained $selfContainedFlag -o $destino --nologo -v q
    }

    # O appsettings.Development.json aponta para localhost e nunca deve ir para a
    # VM: se ele viajasse junto e alguém subisse com ASPNETCORE_ENVIRONMENT=Development,
    # os serviços passariam a escutar em localhost e ficariam inalcançáveis de fora
    # — falha silenciosa e difícil de enxergar.
    $devSettings = Join-Path $destino 'appsettings.Development.json'
    if (Test-Path $devSettings) {
        Remove-Item $devSettings -Force
        Write-Host "    removido appsettings.Development.json do pacote" -ForegroundColor DarkGray
    }

    $tamanho = [math]::Round((Get-ChildItem $destino -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB, 1)
    Write-Host "    $destino  ($tamanho MB)" -ForegroundColor Green
}

if (-not $SkipSql) {
    $migrations = @(
        @{ Arquivo = '01-identity.sql'; Projeto = 'src/Identity/TodoList.Identity.Infrastructure'; Startup = 'src/Identity/TodoList.Identity.Api' }
        @{ Arquivo = '02-tasks.sql';    Projeto = 'src/Tasks/TodoList.Tasks.Infrastructure';       Startup = 'src/Tasks/TodoList.Tasks.Api' }
    )

    foreach ($migration in $migrations) {
        $saida = Join-Path $sqlRoot $migration.Arquivo

        Write-Etapa "Gerando $($migration.Arquivo) (idempotente)"
        # --configuration Release --no-build reaproveita o build que já foi feito
        # no início deste script. Não é só economia de tempo: sem isso o `dotnet ef`
        # compila em Debug, e se os serviços estiverem rodando localmente (o caso
        # normal — você acabou de ensaiar antes de publicar) o executável em
        # bin/Debug está travado pelo processo e o build falha com MSB3027, um erro
        # que não tem nada a ver com migration nenhuma.
        Invoke-Verificado "script de migration $($migration.Arquivo)" {
            dotnet ef migrations script --idempotent `
                --project $migration.Projeto --startup-project $migration.Startup `
                --configuration Release --no-build `
                --output $saida
        }

        Write-Host "    $saida" -ForegroundColor Green
    }
}

# Um arquivo só, porque o SSH do navegador do GCP sobe um arquivo por vez e não
# sobe diretórios. Os arquivos de deploy entram no tarball junto dos binários:
# assim o que está na VM e o que está no repositório nunca divergem por alguém ter
# copiado só metade.
Write-Etapa 'Montando o tarball de deploy'

$stage = Join-Path $OutputRoot '.stage'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage -Force | Out-Null

Copy-Item $publishRoot -Destination (Join-Path $stage 'publish') -Recurse
Copy-Item $sqlRoot -Destination (Join-Path $stage 'sql') -Recurse
Copy-Item (Join-Path $root 'deploy/todolist-identity.service') -Destination $stage
Copy-Item (Join-Path $root 'deploy/todolist-tasks.service') -Destination $stage
Copy-Item (Join-Path $root 'deploy/todolist-gateway.service') -Destination $stage
Copy-Item (Join-Path $root 'deploy/install-on-vm.sh') -Destination $stage
Copy-Item (Join-Path $root 'deploy/smoke.sh') -Destination $stage
Copy-Item (Join-Path $root 'deploy/demo.sh') -Destination $stage
Copy-Item (Join-Path $root 'deploy/tmux-demo.sh') -Destination $stage
Copy-Item (Join-Path $root 'deploy/identity.env.example') -Destination $stage
Copy-Item (Join-Path $root 'deploy/tasks.env.example') -Destination $stage
Copy-Item (Join-Path $root 'deploy/gateway.env.example') -Destination $stage

$tarball = Join-Path $OutputRoot 'todolist-deploy.tar.gz'
if (Test-Path $tarball) { Remove-Item $tarball -Force }

# O tar do Windows 10+ (bsdtar) grava permissões que o Linux entende, mas o bit de
# execução dos .sh não sobrevive de forma confiável vindo do NTFS — por isso o
# runbook manda dar chmod +x depois de extrair, em vez de confiar no arquivo.
Invoke-Verificado 'tar' { tar -czf $tarball -C $stage . }

Remove-Item $stage -Recurse -Force

$tamanhoTar = [math]::Round((Get-Item $tarball).Length / 1MB, 1)

Write-Host ""
Write-Host 'Pacote pronto.' -ForegroundColor Green
Write-Host ''
if ($avisoDocker) {
    Write-Host '  ATENÇÃO: o Docker não estava disponível — os testes de integração com' -ForegroundColor Yellow
    Write-Host '  Postgres real NÃO rodaram. Suba o Docker e rode `dotnet test` antes de' -ForegroundColor Yellow
    Write-Host '  publicar uma versão que vá para a apresentação.' -ForegroundColor Yellow
    Write-Host ''
}
Write-Host "  $tarball  ($tamanhoTar MB)   <- é este que sobe para a VM"
Write-Host "  $publishRoot"
Write-Host "  $sqlRoot"
Write-Host ''
if (-not $SelfContained) {
    Write-Host '  Modo framework-dependent: a VM precisa do runtime ASP.NET Core 10.' -ForegroundColor Yellow
    Write-Host '  O deploy/README.md tem o apt-get, no passo 3.'
    Write-Host ''
}
Write-Host '  Próximo passo: deploy/README.md'
Write-Host ''
