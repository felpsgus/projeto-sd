#Requires -Version 7
<#
.SYNOPSIS
    Confere a rota do SPA e dispara os passos de BE-39 (mais o caso do
    token adulterado, BE-39 emenda de 21/09) contra o API Gateway — o
    roteiro de verificação do T2, automatizado, equivalente do
    deploy/smoke.sh para o notebook Windows.

.DESCRIPTION
    Exercita, contra os três serviços no ar (Identity, Tasks, Gateway) e,
    quando -BaseUrl aponta para fora do Gateway direto, contra o nginx que
    fica na frente deles (BE-42), a sequência que prova os sete requisitos
    de t2.md — frontend, REST público, gRPC interno, banco real, validação
    na borda (400/201), autenticação (401) e tradução JSON -> gRPC:

        0. GET /tasks (rota profunda do SPA)      -> 200 com o index.html do Angular
        1. POST /api/tasks sem token               -> 401 auth.unauthorized
        2. POST /api/tasks com token lixo           -> 401 auth.unauthorized
        2b. POST /api/tasks com token adulterado    -> 401 auth.unauthorized
        3. POST /api/auth/register (e-mail novo)    -> 201, cria a conta usada no resto do roteiro
        4. POST /api/auth/login (conta recém-criada) -> 200 accessToken/expiresAt
        5. POST /api/tasks, token do passo 4, título vazio -> 400
        6. POST /api/tasks, mesmo token, título válido     -> 201 + Location
        7. POST /api/auth/login (usuário inativo)   -> 401 auth.invalid_credentials
                                                        (corpo idêntico ao de senha errada;
                                                        PULADO, com aviso, se -InactiveEmail
                                                        não for informado)

    Os passos 1, 2 e 2b entram separados de propósito: são três caminhos de
    código possivelmente diferentes no middleware de autenticação — token
    ausente, token que nem parece um JWT, e um JWT real com a assinatura
    RS256 quebrada por uma alteração de um caractere (CA-05 de BE-39; 2b
    exercita especificamente a verificação de assinatura de BE-40, que
    "token lixo" não aciona). O passo 7 prova RN-AUTH-09 (usuário inativo
    não é distinguível de senha errada). O passo 0 prova BE-42 CA-01 — sem
    ele, um F5 numa rota profunda do Angular voltaria 404 do nginx antes de
    o roteador do Angular sequer carregar.

    É este script — apontado para o IP externo da maquina-1-psd, SEM porta
    (80, via nginx, desde BE-42) — que roda no dia da apresentação, num
    segundo terminal, a partir de fora da VM (BE-39 CA-04).

    Onda E (T2): o seed de demonstração (DemoUserSeeder,
    ada.lovelace@todolist.example/charles.babbage@todolist.example) foi
    removido do produto — não faz mais sentido existir um atalho de "usuário
    já pronto" quando o cadastro é real (POST /api/auth/register). O passo 3
    cadastra uma conta nova a cada execução, com e-mail gerado a partir do
    relógio (nunca fixo): repetir o ensaio e depois a apresentação, sem
    reiniciar o banco entre as duas execuções, não pode falhar com 409 de
    e-mail já cadastrado — e é exatamente esse o cenário real (ensaio +
    apresentação no mesmo dia, contra o mesmo banco).

    O passo 7 (usuário inativo) não tem mais um usuário pronto para testar —
    não existe rota para desativar uma conta pela API (decisão consciente da
    Onda E: isso seria superfície de negócio nova, fora de escopo). A conta
    inativa precisa ser criada por você, uma vez, com um cadastro comum
    seguido de um UPDATE direto no banco — ver deploy/README.md, seção "No
    dia da apresentação". Depois de existir, informe o e-mail dela em
    -InactiveEmail para o passo 7 rodar; sem isso, o passo é PULADO com um
    aviso explícito (nunca falha silenciosamente, nunca finge ter passado).

.PARAMETER BaseUrl
    Endereço a verificar. Três usos, três valores diferentes — o parâmetro
    é sempre explícito, não há um único "certo":

      - Backend local, sem frontend no ar (padrão desta task): o Gateway
        direto, http://localhost:8080 — não passa por nginx nem por
        ng serve; é o mais rápido para iterar no Gateway isoladamente.
      - Backend local, através do proxy que faz o papel do nginx em dev
        (BE-42, frontend/proxy.conf.json): suba `npm start` em frontend/ e
        aponte para http://localhost:4200 — exercita o mesmo caminho de
        mesma origem que a VM tem, sem precisar da VM.
      - Dia da apresentação / VM real: http://<ip-externo-da-vm>, SEM porta
        — a porta pública mudou de 8080 para 80 (nginx) desde BE-42; 8080
        deixou de responder de fora (verificação de BE-42 CA-03).

.PARAMETER Password
    Senha da conta que o passo 3 cadastra (e, se -InactiveEmail for
    informado, também a senha da conta inativa). Obrigatória — por
    parâmetro ou pela variável de ambiente DEMO_PASSWORD. Nunca fixada
    neste script versionado. Precisa satisfazer a política de senha
    (RN-AUTH-04: 8+ caracteres, ao menos uma letra e um número).

.PARAMETER InactiveEmail
    E-mail de uma conta já desativada por UPDATE direto no banco (ver
    deploy/README.md). Opcional: sem ele, o passo 7 (usuário inativo,
    RN-AUTH-09) é PULADO com um aviso — nunca falha silenciosamente, nunca
    é contado como sucesso.

.PARAMETER IncluindoIndisponibilidade
    Acrescenta um passo extra: com o Identity supostamente fora do ar,
    espera-se 503 com Retry-After (nunca 401). Só faz sentido localmente,
    onde dá para derrubar o processo do Identity antes de rodar; contra a VM,
    isso pararia o serviço para todo mundo — não use no dia da apresentação.

.EXAMPLE
    $env:DEMO_PASSWORD = "..."
    ./scripts/demo-t2.ps1
    ./scripts/demo-t2.ps1 -BaseUrl http://34.10.20.30:8080 -Password "..."
    ./scripts/demo-t2.ps1 -InactiveEmail "inativo@todolist.example"   # depois do UPDATE documentado
    ./scripts/demo-t2.ps1 -IncluindoIndisponibilidade   # local, com o Identity já parado
#>
[CmdletBinding()]
param(
    [string]$BaseUrl = 'http://localhost:8080',
    [string]$Password = $env:DEMO_PASSWORD,
    [string]$InactiveEmail = $env:DEMO_INACTIVE_EMAIL,
    [switch]$IncluindoIndisponibilidade
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Password)) {
    Write-Host 'Defina -Password ou a variável de ambiente DEMO_PASSWORD.' -ForegroundColor Red
    Write-Host '  $env:DEMO_PASSWORD = "sua-senha-de-demo"; ./scripts/demo-t2.ps1' -ForegroundColor DarkGray
    exit 1
}

# E-mail novo a cada execução (até o milissegundo) — cadastro real (POST
# /api/auth/register), não mais o seed fixo removido na Onda E. Rodar o
# script duas vezes seguidas (ensaio, depois apresentação) não pode colidir
# com 409 de e-mail já cadastrado.
$emailNovo = "demo-t2-$(Get-Date -Format 'yyyyMMddHHmmssfff')@todolist.example"

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
Write-Host "Origem verificada: $BaseUrl" -ForegroundColor Cyan
Write-Host "Conta desta execução: $emailNovo" -ForegroundColor DarkGray

# 0. Rota profunda do SPA -> 200 com o index.html do Angular, não 404
# (BE-42 CA-01). Só faz sentido quando BaseUrl serve o frontend (nginx na
# VM, ou ng serve em http://localhost:4200) — contra o Gateway puro
# (porta 8080) não existe rota /tasks nenhuma para responder, então o passo
# é pulado (não contado como falha) nesse caso específico.
if ($BaseUrl -match ':8080/?$') {
    Write-Host ""
    Write-Host "--- 0. GET /tasks (rota profunda do SPA)" -ForegroundColor Cyan
    Write-Host "    PULADO — BaseUrl aponta direto para o Gateway (porta 8080), que não serve o frontend." -ForegroundColor DarkGray
}
else {
    try {
        $respostaSpa = Invoke-WebRequest -Uri "$BaseUrl/tasks" -Method Get -SkipHttpErrorCheck -TimeoutSec 15
        $statusSpa = [int]$respostaSpa.StatusCode
        $corpoSpa = ConvertTo-TextoCorpo -Resposta $respostaSpa
        if ($statusSpa -eq 200 -and $corpoSpa -match '<app-root') {
            Write-Host ""
            Write-Host "--- 0. GET /tasks (rota profunda do SPA)" -ForegroundColor Cyan
            Write-Host "    OK   HTTP 200 (index.html do Angular)" -ForegroundColor Green
        }
        else {
            Write-Host ""
            Write-Host "--- 0. GET /tasks (rota profunda do SPA)" -ForegroundColor Cyan
            Write-Host "    ERRO HTTP $statusSpa (esperado 200 com <app-root> no corpo)" -ForegroundColor Red
            $falhas++
        }
    }
    catch {
        Write-Host ""
        Write-Host "--- 0. GET /tasks (rota profunda do SPA)" -ForegroundColor Cyan
        Write-Host "    FALHA: $($_.Exception.Message)" -ForegroundColor Red
        $falhas++
    }
}

if ($IncluindoIndisponibilidade) {
    Write-Host 'Cenário de indisponibilidade — o Identity precisa estar DESLIGADO.' -ForegroundColor Yellow

    Invoke-Passo -Passo '8. POST /api/tasks com o Identity fora do ar (D-28)' `
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

    # 3. Cadastro real (POST /api/auth/register) — substitui o antigo login
    # direto contra o usuário do seed (removido, Onda E). E-mail novo a cada
    # execução, então repetir o script não esbarra num 409.
    $corpoRegistro = @{ email = $emailNovo; password = $Password; displayName = 'Demo T2' } | ConvertTo-Json -Compress
    Invoke-Passo -Passo '3. POST /api/auth/register (conta nova)' `
        -Metodo 'Post' -Caminho '/api/auth/register' `
        -Headers @{} -Corpo $corpoRegistro `
        -StatusEsperado 201 | Out-Null

    $corpoLogin = @{ email = $emailNovo; password = $Password } | ConvertTo-Json -Compress
    $login = Invoke-Passo -Passo '4. POST /api/auth/login (conta recém-cadastrada)' `
        -Metodo 'Post' -Caminho '/api/auth/login' `
        -Headers @{} -Corpo $corpoLogin `
        -StatusEsperado 200

    $token = $null
    if ($login -and $login.Status -eq 200) {
        $token = ($login.Conteudo | ConvertFrom-Json).accessToken
    }
    if (-not $token) {
        Write-Host "        Sem accessToken no corpo do passo 4 — os passos 5 e 6 vão falhar em cascata." -ForegroundColor Yellow
        $token = 'sem-token-do-passo-4'
    }
    else {
        # 2b. Token adulterado -> 401 (um JWT real, do passo 4, com o
        # último caractere trocado — exercita a verificação de assinatura
        # RS256 do AddJwtBearer, BE-40, que "token lixo" no passo 2 não
        # aciona porque nem chega a parecer um JWT).
        $ultimoCaractere = $token.Substring($token.Length - 1)
        $substituto = if ($ultimoCaractere -eq 'A') { 'B' } else { 'A' }
        $tokenAdulterado = $token.Substring(0, $token.Length - 1) + $substituto

        Invoke-Passo -Passo '2b. POST /api/tasks com token adulterado' `
            -Metodo 'Post' -Caminho '/api/tasks' `
            -Headers @{ Authorization = "Bearer $tokenAdulterado" } -Corpo '{"title":"smoke token adulterado"}' `
            -StatusEsperado 401 -ErrorCodeEsperado 'auth.unauthorized' | Out-Null
    }

    Invoke-Passo -Passo '5. POST /api/tasks com título vazio' `
        -Metodo 'Post' -Caminho '/api/tasks' `
        -Headers @{ Authorization = "Bearer $token" } -Corpo '{"title":""}' `
        -StatusEsperado 400 | Out-Null

    $corpoTarefa = @{ title = 'Verificacao demo-t2.ps1' } | ConvertTo-Json -Compress
    $criacao = Invoke-Passo -Passo '6. POST /api/tasks válido' `
        -Metodo 'Post' -Caminho '/api/tasks' `
        -Headers @{ Authorization = "Bearer $token" } -Corpo $corpoTarefa `
        -StatusEsperado 201

    if ($criacao -and $criacao.Status -eq 201) {
        $location = $criacao.Headers['Location']
        if ($location) {
            Write-Host "    OK   6b. Location: $location" -ForegroundColor Green
        }
        else {
            Write-Host "    ERRO 6b. Header Location ausente na resposta 201" -ForegroundColor Red
            $falhas++
        }
    }

    # 7. Usuário inativo (RN-AUTH-09) — não há mais um usuário pronto para
    # isto (o seed saiu, Onda E, e não existe rota para desativar conta pela
    # API de propósito). Só roda se -InactiveEmail foi informado, apontando
    # para uma conta cadastrada e depois desativada por UPDATE direto no
    # banco (deploy/README.md, "No dia da apresentação"). Sem isso, o passo é
    # PULADO com aviso — nunca falha silenciosamente, nunca finge sucesso.
    Write-Host ""
    Write-Host "--- 7. POST /api/auth/login (usuário inativo)" -ForegroundColor Cyan
    if ([string]::IsNullOrWhiteSpace($InactiveEmail)) {
        Write-Host "    PULADO — nenhum -InactiveEmail informado. Sem ele, este passo não exercita RN-AUTH-09." -ForegroundColor Yellow
        Write-Host "         Cadastre uma conta e desative-a por SQL (deploy/README.md, 'No dia da" -ForegroundColor DarkGray
        Write-Host "         apresentação'), depois rode de novo com -InactiveEmail <email-dessa-conta>." -ForegroundColor DarkGray
    }
    else {
        $corpoLoginInativo = @{ email = $InactiveEmail; password = $Password } | ConvertTo-Json -Compress
        $resultadoInativo = Invoke-WebRequest -Uri "$BaseUrl/api/auth/login" -Method Post `
            -Headers @{} -Body $corpoLoginInativo -ContentType 'application/json' `
            -SkipHttpErrorCheck -TimeoutSec 15
        $statusInativo = [int]$resultadoInativo.StatusCode
        $conteudoInativo = ConvertTo-TextoCorpo -Resposta $resultadoInativo
        $errorCodeInativo = $null
        if ($conteudoInativo) {
            $errorCodeInativo = ($conteudoInativo | ConvertFrom-Json).PSObject.Properties['errorCode']?.Value
        }

        if ($statusInativo -eq 401 -and $errorCodeInativo -eq 'auth.invalid_credentials') {
            Write-Host "    OK   HTTP $statusInativo / $errorCodeInativo" -ForegroundColor Green
        }
        else {
            Write-Host "    ERRO HTTP $statusInativo / $errorCodeInativo (esperado: HTTP 401 / auth.invalid_credentials)" -ForegroundColor Red
            Write-Host "         $conteudoInativo" -ForegroundColor DarkGray
            $falhas++
        }
    }
}

Write-Host ""
if ($falhas -eq 0) {
    Write-Host 'Todos os passos responderam como esperado.' -ForegroundColor Green
    Write-Host 'Agora procure, nos três painéis de log (Gateway, Tasks, Identity), o mesmo traceId do passo 6.'
    Write-Host ''
    exit 0
}

Write-Host "$falhas passo(s) fora do esperado — veja acima." -ForegroundColor Red
Write-Host ''
exit 1
