#Requires -Version 7
<#
.SYNOPSIS
    Dispara os quatro desfechos de POST /api/tasks e confere cada um contra o
    resultado esperado — o roteiro de verificação do BE-31, automatizado.

.DESCRIPTION
    Exercita, contra os dois serviços no ar, a mesma requisição mudando apenas o
    header X-User-Id:

        dono ativo       -> 201 Created
        dono inexistente -> 404 task.owner_not_found
        dono inativo     -> 409 task.owner_inactive
        header ausente   -> 400

    O par sucesso/rejeição é o que demonstra que quem decide é o Identity: um Tasks
    que aceitasse tudo passaria no primeiro caso e falharia no segundo.

    O caminho de indisponibilidade (503 identity.unavailable) não entra aqui porque
    exige derrubar o Identity — rode com -IncluindoIndisponibilidade depois de
    encerrar a janela do Identity com Ctrl+C.

    Depois de cada chamada, olhe as duas janelas de serviço: a linha do Tasks
    (ValidateUser ... statusCode=OK) e a do Identity (ValidateUser: ... exists=...)
    carregam o MESMO traceId. Esse par é a evidência da ida e volta pela rede.

.PARAMETER BaseUrl
    Endereço do Tasks Service. O padrão é o de desenvolvimento; aponte para o
    túnel IAP ou para a VM ao ensaiar contra o GCP.

.PARAMETER IncluindoIndisponibilidade
    Em vez dos quatro caminhos acima, verifica só o 503 — com o Identity desligado.

.EXAMPLE
    ./scripts/demo-curl.ps1
    ./scripts/demo-curl.ps1 -BaseUrl http://localhost:5100 -IncluindoIndisponibilidade
#>
[CmdletBinding()]
param(
    [string]$BaseUrl = 'http://localhost:5100',
    [switch]$IncluindoIndisponibilidade
)

$ErrorActionPreference = 'Stop'

# Ids fixos do DemoUserSeeder (README, seção do store de usuários).
$donoAtivo = '10000000-0000-0000-0000-000000000001'
$donoInativo = '10000000-0000-0000-0000-000000000002'
$donoInexistente = '99999999-9999-9999-9999-999999999999'

$falhas = 0

function Invoke-CriarTarefa {
    param(
        [string]$Cenario,
        [string]$UserId,
        [string]$Titulo,
        [int]$StatusEsperado,
        [string]$ErrorCodeEsperado
    )

    $headers = @{ 'Content-Type' = 'application/json' }
    if ($UserId) { $headers['X-User-Id'] = $UserId }

    $corpo = @{ title = $Titulo } | ConvertTo-Json -Compress

    Write-Host ""
    Write-Host "--- $Cenario" -ForegroundColor Cyan
    Write-Host "    X-User-Id: $(if ($UserId) { $UserId } else { '(ausente)' })"

    try {
        $resposta = Invoke-WebRequest -Uri "$BaseUrl/api/tasks" -Method Post `
            -Headers $headers -Body $corpo -SkipHttpErrorCheck -TimeoutSec 15
    }
    catch {
        Write-Host "    FALHA: não foi possível falar com o Tasks em $BaseUrl — $($_.Exception.Message)" -ForegroundColor Red
        $script:falhas++
        return
    }

    $status = [int]$resposta.StatusCode

    # Content vem como string quando o Content-Type é application/json, mas como
    # byte[] quando é application/problem+json (que é o tipo de TODA resposta de
    # erro daqui) — o PowerShell só decodifica sozinho os tipos que reconhece como
    # texto. Sem esta normalização, o corpo dos caminhos 404/409/503 chegaria como
    # uma lista de bytes e o errorCode nunca seria encontrado.
    $conteudo = if ($resposta.Content -is [byte[]]) {
        [System.Text.Encoding]::UTF8.GetString($resposta.Content)
    }
    else {
        $resposta.Content
    }

    $errorCode = $null
    if ($conteudo) {
        # O corpo de sucesso (201) é o DTO da tarefa e não tem errorCode — por isso
        # a ausência da propriedade é um caso normal, não erro de parsing.
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
}

Write-Host ""
Write-Host "Tasks Service: $BaseUrl" -ForegroundColor Cyan

if ($IncluindoIndisponibilidade) {
    Write-Host 'Cenário de indisponibilidade — o Identity precisa estar DESLIGADO.' -ForegroundColor Yellow

    Invoke-CriarTarefa -Cenario 'Identity fora do ar (fail-closed, D-28)' `
        -UserId $donoAtivo -Titulo 'Identity fora do ar' `
        -StatusEsperado 503 -ErrorCodeEsperado 'identity.unavailable'
}
else {
    Invoke-CriarTarefa -Cenario 'Caminho de sucesso — dono existente e ativo' `
        -UserId $donoAtivo -Titulo 'Preparar a demonstracao do T1' `
        -StatusEsperado 201

    Invoke-CriarTarefa -Cenario 'Rejeicao — dono inexistente (a decisao vem do Identity)' `
        -UserId $donoInexistente -Titulo 'Tarefa de um dono que nao existe' `
        -StatusEsperado 404 -ErrorCodeEsperado 'task.owner_not_found'

    Invoke-CriarTarefa -Cenario 'Rejeicao — dono inativo (existe, mas nao esta ativo)' `
        -UserId $donoInativo -Titulo 'Tarefa de um dono inativo' `
        -StatusEsperado 409 -ErrorCodeEsperado 'task.owner_inactive'

    Invoke-CriarTarefa -Cenario 'Header X-User-Id ausente (modo provisorio de BE-29)' `
        -UserId $null -Titulo 'Sem X-User-Id' `
        -StatusEsperado 400
}

Write-Host ""
if ($falhas -eq 0) {
    Write-Host 'Todos os caminhos responderam como esperado.' -ForegroundColor Green
    Write-Host 'Agora olhe as duas janelas de serviço: o mesmo traceId aparece nos dois logs.'
    Write-Host ''
    exit 0
}

Write-Host "$falhas caminho(s) fora do esperado — veja acima." -ForegroundColor Red
Write-Host ''
exit 1
