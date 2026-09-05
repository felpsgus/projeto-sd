# FE-09 — Tela de login

| | |
|---|---|
| **Domínio** | Autenticação |
| **Depende de** | [FE-07](FE-07-roteamento-guards.md) · backend: [BE-09](../backend/BE-09-login.md), [BE-12](../backend/BE-12-bloqueio-tentativas-login.md) |
| **Bloqueia** | FE-10 |
| **Regras cobertas** | RN-AUTH-08, RN-AUTH-09, RN-AUTH-13, RN-USER-04 |
| **Estimativa** | M |

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

- [ ] **CA-01** — Login com credenciais válidas leva a `/tasks` e o cabeçalho passa a exibir o nome do usuário.
- [ ] **CA-02** — Login vindo de rota protegida retorna o usuário **para aquela rota** após autenticar.
- [ ] **CA-03** — O botão de envio fica desabilitado durante a requisição, e clique duplo dispara **uma** chamada.
- [ ] **CA-04** — Campos vazios impedem o envio, com erro por campo (validação local).

### RN-AUTH-09 — indistinguibilidade

- [ ] **CA-05** — Senha incorreta exibe exatamente **"E-mail ou senha inválidos."**
- [ ] **CA-06** — E-mail inexistente exibe **a mesma mensagem, no mesmo lugar, com o mesmo destaque visual**.
- [ ] **CA-07** — Conta inativa (RN-USER-04) exibe **a mesma mensagem** — a tela não menciona conta desativada.
- [ ] **CA-08** — Em nenhum desses casos um campo específico é marcado como inválido: o erro é do formulário, não do e-mail.
- [ ] **CA-09** — A tela **não** faz nenhuma chamada de verificação de e-mail antes do envio.
- [ ] **CA-10** — Um teste compara o DOM renderizado nos três cenários de falha e confirma que o texto e a estrutura da mensagem são idênticos.

### RN-AUTH-13 — bloqueio

- [ ] **CA-11** — Resposta **429** exibe mensagem de excesso de tentativas, distinta da mensagem de credencial inválida.
- [ ] **CA-12** — A mensagem informa o tempo de espera com base no `Retry-After`, com contagem regressiva.
- [ ] **CA-13** — O botão de envio fica desabilitado enquanto o bloqueio dura e é reabilitado ao terminar, sem recarregar a página.
- [ ] **CA-14** — `Retry-After` ausente exibe a mensagem sem tempo específico, sem valor inventado.
- [ ] **CA-15** — O tempo exibido vem da resposta, não de uma constante `15` no código do frontend.

### Contexto e segurança

- [ ] **CA-16** — Chegando por sessão expirada, a tela exibe "sua sessão expirou" **antes** de qualquer tentativa de login.
- [ ] **CA-17** — Chegando por revogação (RN-AUTH-19), exibe a mensagem de sessão encerrada.
- [ ] **CA-18** — **Não existe** link de "esqueci minha senha" na tela (RN-AUTH-22).
- [ ] **CA-19** — Nenhuma senha aparece em storage, URL, `console` ou atributo do DOM (teste de segurança).
- [ ] **CA-20** — Campos usam `autocomplete="username"` e `autocomplete="current-password"`.
- [ ] **CA-21** — A mensagem de erro é anunciada por leitor de tela (`aria-live`) e o foco vai para ela ou para o campo de e-mail.
- [ ] **CA-22** — A tela é operável só pelo teclado e usável em 360 px.

## Testes obrigatórios

- Componente (Testing Library): CA-01 a CA-04, CA-11 a CA-18, CA-21.
- **CA-05 a CA-10 são o guardião de RN-AUTH-09 no cliente** — o teste comparativo de CA-10 é obrigatório.
- **CA-19 é teste de segurança obrigatório.**
- Login é o primeiro fluxo crítico do E2E ([FE-22](FE-22-testes-e2e.md)).
