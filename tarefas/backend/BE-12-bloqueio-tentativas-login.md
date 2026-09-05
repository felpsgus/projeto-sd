# BE-12 — Bloqueio temporário por tentativas de login

| | |
|---|---|
| **Domínio** | Autenticação |
| **Depende de** | [BE-09](BE-09-login.md) |
| **Bloqueia** | — |
| **Regras cobertas** | RN-AUTH-13 |
| **Estimativa** | M |

## Objetivo

Após 5 tentativas de login malsucedidas consecutivas para o mesmo e-mail, novas tentativas ficam bloqueadas por 15 minutos.

## Escopo

### Inclui

- `LockoutOptions` tipado:

  | Chave | Padrão |
  |---|---|
  | `Lockout:MaxAttempts` | **5** |
  | `Lockout:LockoutMinutes` | **15** |
  | `Lockout:AttemptWindowMinutes` | **15** (janela em que as tentativas são consideradas "consecutivas") |
  | `Lockout:Enabled` | `true` |

- Registro de tentativas por **e-mail normalizado** (D-14), persistido: contador, instante da última tentativa, `LockedUntil`.
- Integração no `LoginHandler` (BE-09):
  - **antes** de verificar credenciais, checar bloqueio → se bloqueado, falha imediatamente;
  - falha de credencial → incrementa o contador; ao atingir `MaxAttempts`, define `LockedUntil = agora + LockoutMinutes`;
  - **sucesso → zera o contador** e limpa o bloqueio.
- Resposta de bloqueio: **429 Too Many Requests** com `Retry-After` em segundos e código `auth.too_many_attempts`.
- Expiração automática: passado `LockedUntil`, a conta volta a aceitar tentativas sem intervenção.

### Não inclui

- Bloqueio por IP ou rate limiting global do endpoint (D-14 adotou por e-mail, seguindo a redação da RN). Se o time decidir somar IP, é task nova.
- CAPTCHA, notificação por e-mail (fora do escopo, seção 8).

## Notas técnicas

- **Tensão real com RN-AUTH-09:** responder 429 confirma que aquele e-mail está sendo protegido — o que insinua que ele existe. Duas saídas:
  1. **contar tentativas para e-mails inexistentes também**, aplicando o bloqueio de forma idêntica (adotado);
  2. responder sempre 401.
  Adotamos (1): um e-mail inexistente que recebe 5 tentativas também passa a responder 429. Assim o 429 não distingue conta existente de inexistente, e a RN-AUTH-13 é cumprida literalmente. **Esta escolha precisa estar num ADR.**
- Contador em **banco**, não em memória: memória se perde no restart e não funciona com múltiplas instâncias.
- Concorrência: 5 tentativas paralelas não podem "passar" pelo limite. Usar incremento atômico no banco.
- `TimeProvider` para tudo — os testes avançam o relógio, nunca dormem.

## Critérios de aceite

- [ ] **CA-01** — Após **4** falhas consecutivas, a 5ª tentativa ainda é processada (retorna 401 se a senha estiver errada).
- [ ] **CA-02** — Após **5** falhas consecutivas, a 6ª tentativa retorna **429**, mesmo com a **senha correta** (RN-AUTH-13).
- [ ] **CA-03** — A resposta 429 traz o cabeçalho `Retry-After` com o tempo restante em segundos.
- [ ] **CA-04** — Passados 15 minutos (relógio avançado), a tentativa seguinte é processada normalmente e o login com senha correta **sucede**.
- [ ] **CA-05** — Um login **bem-sucedido** zera o contador: após 4 falhas + 1 sucesso, são necessárias 5 novas falhas para bloquear.
- [ ] **CA-06** — O bloqueio é **por e-mail**: bloquear `a@x.com` não afeta o login de `b@x.com`.
- [ ] **CA-07** — O bloqueio é case-insensitive: tentativas em `A@X.com` e `a@x.com` somam no mesmo contador.
- [ ] **CA-08** — Cinco tentativas falhas para um e-mail **inexistente** também levam a 429 — a resposta de bloqueio não distingue conta existente de inexistente (RN-AUTH-09 preservada).
- [ ] **CA-09** — Tentativas espaçadas além de `AttemptWindowMinutes` **não** acumulam: 4 falhas, espera de 20 min, 2 falhas → não bloqueia.
- [ ] **CA-10** — 10 tentativas **concorrentes** com senha errada não permitem mais que `MaxAttempts` verificações de senha; o bloqueio é aplicado corretamente (teste de concorrência).
- [ ] **CA-11** — O contador sobrevive a um reinício da aplicação (está no banco, não em memória).
- [ ] **CA-12** — Com `Lockout:Enabled = false`, nenhuma tentativa é bloqueada (útil para ambiente de teste de carga).
- [ ] **CA-13** — Alterar `MaxAttempts` para 3 na configuração faz o bloqueio ocorrer na 4ª tentativa, **sem mudança de código**.
- [ ] **CA-14** — Nenhum log de tentativa contém a senha; o e-mail pode ser logado.

## Testes obrigatórios

- Unidade: política de bloqueio com `TimeProvider` fake — CA-01, CA-02, CA-04, CA-05, CA-09, CA-13.
- Integração: CA-03, CA-06 a CA-08, CA-10 a CA-12.
- ADR sobre a escolha de contar e-mails inexistentes.

## Decisões em aberto

- **D-03** — 5 tentativas → 15 minutos. Padrão adotado, configurável.
- **D-14** — Contagem por e-mail (não por IP). Padrão provisório.
