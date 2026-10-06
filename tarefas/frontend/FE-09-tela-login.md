# FE-09 — Tela de login

| | |
|---|---|
| **Domínio** | Autenticação |
| **Depende de** | [FE-07](FE-07-roteamento-guards.md) · backend: [BE-09](../backend/BE-09-login.md), [BE-12](../backend/BE-12-bloqueio-tentativas-login.md) |
| **Bloqueia** | FE-10 |
| **Regras cobertas** | RN-AUTH-08, RN-AUTH-09, RN-AUTH-13, RN-USER-04 |
| **Estimativa** | M |

> **Recorte do T2 (21/09/2026):** entra **integral**, exceto o bloco de bloqueio por tentativas (RN-AUTH-13, CA-11 a CA-15), que depende de [BE-12](../backend/BE-12-bloqueio-tentativas-login.md) — o backend do T2 não implementa 429. A indistinguibilidade de mensagem (RN-AUTH-09, CA-05 a CA-10) e a ausência de "esqueci minha senha" continuam integrais e são as mesmas que a demo do T2 usa para mostrar o 401. Também não há e-mail pré-preenchido vindo do cadastro (FE-08 fora do T2) nem mensagem de sessão revogada (depende de [FE-06](FE-06-interceptor-auth-refresh.md) integral, fora do T2). Depende de [BE-33](../backend/BE-33-login-minimo-grpc.md)/[BE-36](../backend/BE-36-api-gateway.md).
>
> **Contrato real do 401 de login extraído do Gateway** (`Endpoints/AuthEndpoints.cs`): `ProblemDetails` com `status: 401`, `title: "Credenciais inválidas."`, `detail: "E-mail ou senha inválidos."` e `extensions.errorCode: "auth.invalid_credentials"` — idêntico para e-mail inexistente, senha errada ou conta inativa (o Gateway não distingue os três casos).

## Objetivo

O usuário autentica com e-mail e senha e é levado ao seu destino — e a tela não revela mais do que deve quando as credenciais falham.

## Escopo

### Inclui

- Rota `/login` no `AuthLayout`, protegida por `guestGuard`.
- Formulário Signal Forms com **e-mail** e **senha**, ambos obrigatórios (RN-AUTH-08).
- Envio a `POST /api/auth/login`; sucesso → `SessionStore.startSession(...)` e navegação para a `returnUrl` validada ou `/tasks`.
- **Mensagem única de falha** (RN-AUTH-09): qualquer 401 exibe **"E-mail ou senha inválidos."** — sem variação por cenário, sem dica adicional, sem destacar um campo em vez do outro.
- **Bloqueio por tentativas** (RN-AUTH-13): resposta **429** exibe mensagem informando que houve muitas tentativas e **quando** será possível tentar de novo, usando o `Retry-After` da resposta. Contagem regressiva visível; botão de envio desabilitado até o fim.
- Mensagem de contexto quando o usuário chega redirecionado: "sua sessão expirou" ou "sua sessão foi encerrada" ([FE-06](FE-06-interceptor-auth-refresh.md)).
- E-mail pré-preenchido quando vem do cadastro ([FE-08](FE-08-tela-cadastro.md)).
- Link para "criar conta".
- **Ausência deliberada** de link "esqueci minha senha" — RN-AUTH-22 coloca isso fora do escopo. Não exibir um link que não funciona.

### Não inclui

- Recuperação de senha (RN-AUTH-22 / D-04).
- Login social, 2FA (fora do escopo, seção 8).
- "Lembrar-me" — a duração da sessão é definida pelo refresh token de 7 dias (RN-AUTH-15).

## Notas técnicas

- **RN-AUTH-09 é fácil de quebrar no frontend mesmo com o backend correto.** Três formas de vazar a informação que o backend protegeu:
  1. marcar o campo de e-mail como inválido em vez do formulário inteiro ("o problema é o e-mail");
  2. exibir mensagem diferente para conta inativa (RN-USER-04) — o backend devolve o mesmo 401, e a tela não pode inventar distinção;
  3. verificar a existência do e-mail antes do envio (não existe esse endpoint — e não deve existir).
  CA-05 a CA-08 travam isso.
- **A tensão do 429:** o backend conta tentativas também para e-mails inexistentes ([BE-12](../backend/BE-12-bloqueio-tentativas-login.md)), justamente para o 429 não revelar quais contas existem. O frontend só exibe o que recebeu — não deduz nada a partir do status.
- A contagem regressiva usa o `Retry-After`; se ele vier ausente, exibir a mensagem sem tempo específico em vez de chutar 15 minutos.
- `autocomplete`: `username` no e-mail, `current-password` na senha — assim o gerenciador de senhas funciona.

## Critérios de aceite

### Fluxo

- [x] **CA-01** — Login com credenciais válidas leva a `/tasks` e o cabeçalho passa a exibir o nome do usuário.
- [x] **CA-02** — Login vindo de rota protegida retorna o usuário **para aquela rota** após autenticar.
- [x] **CA-03** — O botão de envio fica desabilitado durante a requisição, e clique duplo dispara **uma** chamada.
- [x] **CA-04** — Campos vazios impedem o envio, com erro por campo (validação local).

### RN-AUTH-09 — indistinguibilidade

- [x] **CA-05** — Senha incorreta exibe exatamente **"E-mail ou senha inválidos."**
- [x] **CA-06** — E-mail inexistente exibe **a mesma mensagem, no mesmo lugar, com o mesmo destaque visual**.
- [x] ~~**CA-07** — Conta inativa (RN-USER-04) exibe **a mesma mensagem** — a tela não menciona conta desativada.~~ **Substituído (03/10/2026)** pela remoção do usuário inativo (issue #16).
- [x] **CA-08** — Em nenhum desses casos um campo específico é marcado como inválido: o erro é do formulário, não do e-mail.
- [x] **CA-09** — A tela **não** faz nenhuma chamada de verificação de e-mail antes do envio.
- [x] **CA-10** — Um teste compara o DOM renderizado nos três cenários de falha e confirma que o texto e a estrutura da mensagem são idênticos.

### RN-AUTH-13 — bloqueio

- [x] **CA-11** — Resposta **429** exibe mensagem de excesso de tentativas, distinta da mensagem de credencial inválida.
- [x] **CA-12** — A mensagem informa o tempo de espera com base no `Retry-After`, com contagem regressiva. *(Atendido em 03/10/2026, issue #12: a mensagem é recontada por minuto, não por segundo, para não fazer o leitor de tela anunciar sem parar.)*
- [x] **CA-13** — O botão de envio fica desabilitado enquanto o bloqueio dura e é reabilitado ao terminar, sem recarregar a página. *(Atendido em 03/10/2026, issue #12, com um refinamento: o bloqueio é do e-mail (ADR-0002), então o botão só fica desabilitado enquanto o campo contém o e-mail bloqueado; trocar o e-mail reabilita. Sem `Retry-After` o botão fica habilitado, por não haver como saber quando reabilitar.)*
- [x] **CA-14** — `Retry-After` ausente exibe a mensagem sem tempo específico, sem valor inventado.
- [x] **CA-15** — O tempo exibido vem da resposta, não de uma constante `15` no código do frontend.

### Contexto e segurança

- [x] **CA-16** — Chegando por sessão expirada, a tela exibe "sua sessão expirou" **antes** de qualquer tentativa de login.
- [x] **CA-17** — Chegando por revogação (RN-AUTH-19), exibe a mensagem de sessão encerrada.
- [x] **CA-18** — **Não existe** link de "esqueci minha senha" na tela (RN-AUTH-22).
- [x] **CA-19** — Nenhuma senha aparece em storage, URL, `console` ou atributo do DOM (teste de segurança).
- [x] **CA-20** — Campos usam `autocomplete="username"` e `autocomplete="current-password"`.
- [x] **CA-21** — A mensagem de erro é anunciada por leitor de tela (`aria-live`) e o foco vai para ela ou para o campo de e-mail.
- [x] **CA-22** — A tela é operável só pelo teclado e usável em 360 px.

## Testes obrigatórios

- Componente (Testing Library): CA-01 a CA-04, CA-11 a CA-18, CA-21.
- **CA-05 a CA-10 são o guardião de RN-AUTH-09 no cliente** — o teste comparativo de CA-10 é obrigatório.
- **CA-19 é teste de segurança obrigatório.**
- Login é o primeiro fluxo crítico do E2E ([FE-22](FE-22-testes-e2e.md)).

## Auditoria dos critérios (03/10/2026)

Critérios conferidos contra o código em 03/10/2026. Marcados: 16 de 22.

| CA | Situação | Evidência / motivo |
|---|---|---|
| CA-02 | em aberto | Sem teste de navegação à `returnUrl` após o login (código em `LoginComponent.submit`); só o E2E "cadastro, login" cobre o destino padrão `/tasks`. |
| CA-03 | em aberto | `disabled`/`aria-busy` durante o envio e a guarda `submitting()` existem, mas nenhum teste de login verifica o botão desabilitado nem o clique duplo. |
| CA-10 | em aberto | O teste "CA-05 a CA-10" itera sobre um array de um único código (`auth.invalid_credentials`); não compara o DOM de três cenários, como o critério exige. |
| CA-12 | em aberto | O tempo vem de `Retry-After` (testado: 540 s vira "9 minutos"), mas não há contagem regressiva. |
| CA-13 | em aberto (decisão de desenho) | O botão não é desabilitado durante o bloqueio; o teste afirma o oposto ("a tela não trava"). Não atendido. |
| CA-21 | em aberto | `role="alert"`/`aria-live="assertive"` e `focus()` do erro estão no código; o teste só verifica `role="alert"`, não o foco. |

CA-06/CA-07 atendidos por desenho (o 401 é idêntico, sem tratamento por cenário), mas o teste correspondente só exerce um cenário.

## Emenda (03/10/2026) — usuário inativo removido

A tela de login não trata conta inativa (RN-USER-04 removida, issue #16): o backend não tem mais esse caso. O CA-07 está substituído; as falhas restantes (credencial inválida, bloqueio, rede) seguem como especificado.
