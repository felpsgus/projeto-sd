#Requires -Version 7
<#
.SYNOPSIS
    Dispara os seis passos de BE-39 contra o API Gateway e confere cada um
    contra o resultado esperado — o roteiro de verificação do T2, automatizado,
    equivalente do deploy/smoke.sh para o notebook Windows.

.DESCRIPTION
    Exercita, contra os três serviços no ar (Identity, Tasks, Gateway),
    a sequência que prova os quatro requisitos de t2.md — REST público,
    validação na borda (400/201), autenticação (401) e tradução JSON -> gRPC:

        1. POST /api/tasks sem token             -> 401 auth.unauthorized
        2. POST /api/tasks com token lixo         -> 401 auth.unauthorized
        3. POST /api/auth/login (usuário ativo)   -> 200 accessToken/expiresAt
        4. POST /api/tasks, token do passo 3, título vazio -> 400
        5. POST /api/tasks, mesmo token, título válido     -> 201 + Location
        6. POST /api/auth/login (usuário inativo) -> 401 auth.invalid_credentials
                                                      (corpo idêntico ao de senha errada)

    Os passos 1 e 2 entram separados de propósito (CA-05): são dois caminhos
    de código possivelmente diferentes no middleware de autenticação — token
    ausente vs. token presente que falha na validação. O passo 6 prova
    RN-AUTH-09 (usuário inativo não é distinguível de senha errada).

    É este script — apontado para o IP externo da maquina-1-psd, porta 8080 —
    que roda no dia da apresentação, a partir de fora da VM (BE-39 CA-04).

.PARAMETER BaseUrl
    Endereço do API Gateway. Padrão: desenvolvimento local. No dia da
    apresentação, aponte para http://<ip-externo-da-vm>:8080.

.PARAMETER DemoPassword
    Senha de UserStore:DemoUserPassword. Obrigatória — por parâmetro ou pela
    variável de ambiente DEMO_PASSWORD. Nunca fixada neste script versionado.

.PARAMETER IncluindoIndisponibilidade
    Acrescenta um sétimo passo: com o Identity supostamente fora do ar,
    espera-se 503 com Retry-After (nunca 401). Só faz sentido localmente,
    onde dá para derrubar o processo do Identity antes de rodar; contra a VM,
    isso pararia o serviço para todo mundo — não use no dia da apresentação.

.EXAMPLE
    $env:DEMO_PASSWORD = "..."
    ./scripts/demo-t2.ps1
    ./scripts/demo-t2.ps1 -BaseUrl http://34.10.20.30:8080 -DemoPassword "..."
    ./scripts/demo-t2.ps1 -IncluindoIndisponibilidade   # local, com o Identity já parado
#>
[CmdletBinding()]
param(
    [string]$BaseUrl = 'http://localhost:8080',
    [string]$DemoPassword = $env:DEMO_PASSWORD,
    [switch]$IncluindoIndisponibilidade
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($DemoPassword)) {
    Write-Host 'Defina -DemoPassword ou a variável de ambiente DEMO_PASSWORD (UserStore:DemoUserPassword).' -ForegroundColor Red
    Write-Host '  $env:DEMO_PASSWORD = "sua-senha-de-demo"; ./scripts/demo-t2.ps1' -ForegroundColor DarkGray
    exit 1
}

# Usuários do seed (DemoUserSeeder) — README, seção "Rodando o T2".
$emailAtivo = 'ada.lovelace@todolist.example'
$emailInativo = 'charles.babbage@todolist.example'

$falhas = 0

function ConvertTo-TextoCorpo {
    param($Resposta)

    # Content vem como string quando o Content-Type é application/json, mas
    # como byte[] quando é application/problem+json (todo corpo de erro daqui)
    # — o PowerShell só decodifica sozinho os tipos que reconhece como texto.
    if ($Resposta.Content -is [byte[]]) {
        return [System.Text.Encoding]::UTF8.GetString($Resposta.Content)
    }

    return $Resposta.Content
}

function Invoke-Passo {
    param(
        [string]$Passo,
        [string]$Metodo,
        [string]$Caminho,
        [hashtable]$Headers,
        [string]$Corpo,
        [int]$StatusEsperado,
        [string]$ErrorCodeEsperado
    )

    Write-Host ""
    Write-Host "--- $Passo" -ForegroundColor Cyan

    try {
        $resposta = Invoke-WebRequest -Uri "$BaseUrl$Caminho" -Method $Metodo `
            -Headers $Headers -Body $Corpo -ContentType 'application/json' `
            -SkipHttpErrorCheck -TimeoutSec 15
    }
    catch {
        Write-Host "    FALHA: não foi possível falar com o Gateway em $BaseUrl — $($_.Exception.Message)" -ForegroundColor Red
        $script:falhas++
        return $null
    }

    $status = [int]$resposta.StatusCode
    $conteudo = ConvertTo-TextoCorpo -Resposta $resposta

    $errorCode = $null
    if ($conteudo) {
        $json = $conteudo | ConvertFrom-Json
        $errorCode = $json.PSObject.Properties['errorCode']?.Value
    }

    $statusOk = $status -eq $StatusEsperado
    $codeOk = (-not $ErrorCodeEsperado) -or ($errorCode -eq $ErrorCodeEsperado)

    $descricao = "HTTP $status" + $(if ($errorCode) { " / $errorCode" } else { '' })

    if ($statusOk -and $codeOk) {
        Write-Host "    OK   $descricao" -ForegroundColor Green
    }
    else {
        $esperado = "HTTP $StatusEsperado" + $(if ($ErrorCodeEsperado) { " / $ErrorCodeEsperado" } else { '' })
        Write-Host "    ERRO $descricao  (esperado: $esperado)" -ForegroundColor Red
        Write-Host "         $conteudo" -ForegroundColor DarkGray
        $script:falhas++
    }

    if ($status -eq 503 -and $resposta.Headers['Retry-After']) {
        Write-Host "         Retry-After: $($resposta.Headers['Retry-After'])" -ForegroundColor DarkGray
    }

    return [pscustomobject]@{
        Status   = $status
        Conteudo = $conteudo
        Headers  = $resposta.Headers
    }
}

Write-Host ""
Write-Host "API Gateway: $BaseUrl" -ForegroundColor Cyan

if ($IncluindoIndisponibilidade) {
    Write-Host 'Cenário de indisponibilidade — o Identity precisa estar DESLIGADO.' -ForegroundColor Yellow

    Invoke-Passo -Passo '7. POST /api/tasks com o Identity fora do ar (D-28)' `
        -Metodo 'Post' -Caminho '/api/tasks' `
        -Headers @{ Authorization = 'Bearer qualquer-token' } -Corpo '{"title":"Identity fora do ar"}' `
        -StatusEsperado 503 | Out-Null
}
else {
    Invoke-Passo -Passo '1. POST /api/tasks sem token' `
        -Metodo 'Post' -Caminho '/api/tasks' `
        -Headers @{} -Corpo '{"title":"smoke sem token"}' `
        -StatusEsperado 401 -ErrorCodeEsperado 'auth.unauthorized' | Out-Null

    Invoke-Passo -Passo '2. POST /api/tasks com token lixo' `
        -Metodo 'Post' -Caminho '/api/tasks' `
        -Headers @{ Authorization = 'Bearer token-lixo-arbitrario' } -Corpo '{"title":"smoke token lixo"}' `
        -StatusEsperado 401 -ErrorCodeEsperado 'auth.unauthorized' | Out-Null

    $corpoLogin = @{ email = $emailAtivo; password = $DemoPassword } | ConvertTo-Json -Compress
    $login = Invoke-Passo -Passo '3. POST /api/auth/login (usuário ativo)' `
        -Metodo 'Post' -Caminho '/api/auth/login' `
        -Headers @{} -Corpo $corpoLogin `
        -StatusEsperado 200

    $token = $null
    if ($login -and $login.Status -eq 200) {
        $token = ($login.Conteudo | ConvertFrom-Json).accessToken
    }
    if (-not $token) {
        Write-Host "        Sem accessToken no corpo do passo 3 — os passos 4 e 5 vão falhar em cascata." -ForegroundColor Yellow
        $token = 'sem-token-do-passo-3'
    }

    Invoke-Passo -Passo '4. POST /api/tasks com título vazio' `
        -Metodo 'Post' -Caminho '/api/tasks' `
        -Headers @{ Authorization = "Bearer $token" } -Corpo '{"title":""}' `
        -StatusEsperado 400 | Out-Null

    $corpoTarefa = @{ title = 'Verificacao demo-t2.ps1' } | ConvertTo-Json -Compress
    $criacao = Invoke-Passo -Passo '5. POST /api/tasks válido' `
        -Metodo 'Post' -Caminho '/api/tasks' `
        -Headers @{ Authorization = "Bearer $token" } -Corpo $corpoTarefa `
        -StatusEsperado 201

    if ($criacao -and $criacao.Status -eq 201) {
        $location = $criacao.Headers['Location']
        if ($location) {
            Write-Host "    OK   5b. Location: $location" -ForegroundColor Green
        }
        else {
            Write-Host "    ERRO 5b. Header Location ausente na resposta 201" -ForegroundColor Red
            $falhas++
        }
    }

    $corpoLoginInativo = @{ email = $emailInativo; password = $DemoPassword } | ConvertTo-Json -Compress
    Invoke-Passo -Passo '6. POST /api/auth/login (usuário inativo)' `
        -Metodo 'Post' -Caminho '/api/auth/login' `
        -Headers @{} -Corpo $corpoLoginInativo `
        -StatusEsperado 401 -ErrorCodeEsperado 'auth.invalid_credentials' | Out-Null
}

Write-Host ""
if ($falhas -eq 0) {
    Write-Host 'Todos os passos responderam como esperado.' -ForegroundColor Green
    Write-Host 'Agora procure, nos três painéis de log (Gateway, Tasks, Identity), o mesmo traceId do passo 5.'
    Write-Host ''
    exit 0
}

Write-Host "$falhas passo(s) fora do esperado — veja acima." -ForegroundColor Red
Write-Host ''
exit 1
