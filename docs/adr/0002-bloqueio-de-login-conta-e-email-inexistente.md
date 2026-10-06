# ADR-0002 — O bloqueio de login conta tentativas de e-mails que não existem

- **Status:** aceita (03/10/2026) — registrada como **D-43** em `tarefas/backend/DECISOES-PENDENTES.md` (fecha D-03 e D-14)
- **Contexto de regra:** RN-AUTH-09, RN-AUTH-13; task BE-12

## Contexto

RN-AUTH-13 bloqueia por 15 minutos o e-mail que acumulou 5 falhas. RN-AUTH-09 exige que o login nunca revele
se um e-mail está cadastrado. As duas regras colidem: se só e-mails **existentes** fossem bloqueados, a
resposta 429 (em vez de 401) diria a quem ataca que aquele e-mail é uma conta real.

## Decisão

Contar as tentativas por **e-mail normalizado** (trim + minúsculas), existente ou não, e aplicar o bloqueio de
forma idêntica. Um e-mail inexistente que recebe 5 tentativas também passa a responder 429.

- A chave de `identity.login_attempts` é o texto do e-mail, sem FK para `users`.
- A contagem é feita **antes** de verificar a senha, num único upsert atômico no banco, e o bloqueio vale
  mesmo para a senha correta.
- E-mail malformado (`Email.Create` falha) **não** é contado: não há chave normalizada confiável, e o
  contador não pode crescer com lixo arbitrário. Ele continua pagando o hash dummy, então o tempo de resposta
  não o distingue dos demais casos.
- O bloqueio é por e-mail, não por IP (D-14). Bloqueio por IP ou rate limit global seria task nova.

## Alternativas consideradas

1. **Contar só e-mails existentes.** Torna o 429 um oráculo de existência de conta. Rejeitada por RN-AUTH-09.
2. **Responder sempre 401, mesmo bloqueado.** Esconde a existência, mas o usuário legítimo não sabe por que a
   senha correta falha, e a regra pede um bloqueio perceptível (`Retry-After`). Rejeitada.
3. **Contar também e-mails inexistentes** (escolhida): o 429 não distingue conta existente de inexistente.

## Consequências

- **Crescimento da tabela.** Um atacante que varre e-mails distintos cria uma linha por e-mail. A linha é
  pequena e some no primeiro login bem-sucedido, mas linhas de e-mails inexistentes só saem por expurgo. Fica
  para a task de expurgo (BE-23) ou um job próprio; não há limpeza automática hoje.
- **Negação de serviço dirigida.** Quem conhece o e-mail de alguém pode mantê-lo bloqueado errando a senha de
  propósito. É o custo inerente ao bloqueio por conta; o limite é de 15 minutos e é configurável
  (`Lockout:MaxAttempts`, `Lockout:LockoutMinutes`, `Lockout:AttemptWindowMinutes`, `Lockout:Enabled`).
- Contagem **antes** da verificação: um login correto na 5ª tentativa ainda passa e zera o contador; as
  tentativas concorrentes que passam do limite são recusadas sem verificar a senha.
- Se o requisito mudar para "por IP" ou "por e-mail + IP", é uma nova ADR que substitui esta.
