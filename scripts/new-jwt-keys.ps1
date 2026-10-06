#Requires -Version 7
<#
.SYNOPSIS
    Gera um par de chaves RSA 2048 bits para assinatura/validação do JWT (BE-40, RS256).

.DESCRIPTION
    Gera o par com .NET puro (System.Security.Cryptography.RSA) — sem depender de
    `openssl` estar instalado na máquina de desenvolvimento — e grava dois arquivos
    PEM no diretório de saída:

        private.pem   PKCS8, RSA privada — só o Identity lê este arquivo (Jwt:PrivateKeyPath)
        public.pem    SubjectPublicKeyInfo, RSA pública — só o Gateway lê este arquivo (Jwt:PublicKeyPath)

    O par gerado aqui é para desenvolvimento local (`dotnet run`) e para o perfil
    `full` do docker-compose — nunca para a VM. Na VM, o par é gerado direto por
    `deploy/install-docker-on-vm.sh` com `openssl`, na própria VM (D-38): nenhuma
    chave sai daqui para lá, e nenhuma chave de teste é reaproveitada em produção.

    Por padrão grava em `<raiz do repositório>/.secrets/jwt` — pasta ignorada pelo
    git (.gitignore: `.secrets/`, `*.pem`). NUNCA versione o conteúdo desta pasta.

.PARAMETER OutDir
    Diretório de saída. Padrão: `.secrets/jwt` na raiz do repositório.

.PARAMETER Force
    Sobrescreve `private.pem`/`public.pem` existentes. Sem esta flag, o script se
    recusa a sobrescrever — trocar a chave invalida todo token já emitido com a
    chave antiga (D-38: sem rotação, sem `kid` secundário nesta etapa).

.EXAMPLE
    ./scripts/new-jwt-keys.ps1
    ./scripts/new-jwt-keys.ps1 -Force
    ./scripts/new-jwt-keys.ps1 -OutDir ./minhas-chaves
#>
[CmdletBinding()]
param(
    [string]$OutDir,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
if (-not $OutDir) { $OutDir = Join-Path $root '.secrets/jwt' }

function Write-Etapa([string]$texto) {
    Write-Host ""
    Write-Host "==> $texto" -ForegroundColor Cyan
}

$privatePath = Join-Path $OutDir 'private.pem'
$publicPath = Join-Path $OutDir 'public.pem'

if ((Test-Path $privatePath) -or (Test-Path $publicPath)) {
    if (-not $Force) {
        throw "Já existe $privatePath ou $publicPath. Use -Force para sobrescrever (isso invalida todo token emitido com a chave atual — D-38)."
    }
    Write-Host "-Force: sobrescrevendo o par existente em $OutDir" -ForegroundColor Yellow
}

Write-Etapa "Gerando par RSA 2048 bits (System.Security.Cryptography, sem depender de openssl)"

$rsa = [System.Security.Cryptography.RSA]::Create(2048)
try {
    $privatePem = $rsa.ExportPkcs8PrivateKeyPem()
    $publicPem = $rsa.ExportSubjectPublicKeyInfoPem()
}
finally {
    $rsa.Dispose()
}

New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

# Sem newline final "\n" extra em relação ao Export*Pem, que já devolve o PEM com
# quebras de linha internas; -NoNewline evita só um CRLF a mais no fim do arquivo.
Set-Content -Path $privatePath -Value $privatePem -NoNewline -Encoding ascii
Set-Content -Path $publicPath -Value $publicPem -NoNewline -Encoding ascii

Write-Host ""
Write-Host "Par gerado:" -ForegroundColor Green
Write-Host "  $privatePath  (PKCS8, RSA privada — só o Identity lê, Jwt:PrivateKeyPath)"
Write-Host "  $publicPath   (SubjectPublicKeyInfo, RSA pública — só o Gateway lê, Jwt:PublicKeyPath)"
Write-Host ""
Write-Host "ATENÇÃO: estes arquivos NUNCA devem ser versionados (.gitignore já ignora" -ForegroundColor Yellow
Write-Host "'.secrets/' e '*.pem'). Não copie este par para a VM nem para nenhum ambiente" -ForegroundColor Yellow
Write-Host "de implantação — lá a chave é gerada direto na VM (deploy/install-docker-on-vm.sh)." -ForegroundColor Yellow
Write-Host ""
