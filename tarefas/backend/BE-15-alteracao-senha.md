# BE-15 — Alteração de senha

| | |
|---|---|
| **Domínio** | Autenticação |
| **Depende de** | [BE-06](BE-06-hash-senha.md), [BE-10](BE-10-refresh-token-rotacao.md), [BE-13](BE-13-protecao-endpoints.md) |
| **Bloqueia** | — |
| **Regras cobertas** | RN-AUTH-21, RN-AUTH-19, RN-AUTH-04 |
| **Estimativa** | M |

## Objetivo

O usuário autenticado troca a própria senha informando a senha atual, e a troca derruba todas as sessões ativas.

## Escopo

### Inclui

- **`POST /api/me/change-password`** (autenticado).
- Request: `{ "currentPassword": "string", "newPassword": "string" }`.
- Caso de uso `ChangePasswordHandler`:
  1. carrega o usuário corrente;
  2. verifica `currentPassword` com `IPasswordHasher.Verify` → falha → **400/401** com `auth.invalid_current_password`;
  3. valida `newPassword` pela política de BE-06 (RN-AUTH-04);
  4. rejeita nova senha **igual** à atual;
  5. gera novo hash e persiste;
  6. **revoga todos os refresh tokens do usuário** com `RevokedReason = PasswordChanged` (RN-AUTH-19).
- Resposta **204 No Content**.
- Tudo em **uma transação**: ou a senha muda e as sessões caem juntas, ou nada acontece.

### Não inclui

- Recuperação de senha esquecida — fora do escopo (RN-AUTH-22 / D-04).
- Reemissão automática de tokens após a troca: o usuário faz login novamente. (Alternativa possível: emitir um novo par na resposta. Não adotada — mantém o efeito de RN-AUTH-19 sem exceção.)

## Notas técnicas

- Exigir a senha atual é o que impede que um access token roubado (válido por até 15 min) seja usado para tomar a conta permanentemente.
- A revogação em massa é o ponto que dá sentido à troca: sem ela, uma sessão comprometida sobreviveria à mudança de senha por até 7 dias.
- O access token corrente **continua válido até expirar** — mesma limitação de BE-11, mesmo ADR.
- Nem a senha atual nem a nova entram em log.

## Critérios de aceite

- [x] **CA-01** — Troca com senha atual correta e nova senha válida retorna **204**.
- [x] **CA-02** — Após a troca, o login com a **senha nova** funciona.
- [x] **CA-03** — Após a troca, o login com a **senha antiga** falha com 401.
- [x] **CA-04** — Senha atual incorreta retorna erro com código `auth.invalid_current_password` e **não altera** o hash no banco.
- [x] **CA-05** — Nova senha fora da política (< 8 caracteres, sem letra, sem número) retorna **400** com os erros por campo (RN-AUTH-04).
- [x] **CA-06** — Nova senha **igual** à atual é rejeitada com **400**.
- [x] **CA-07** — Após a troca, **todos** os refresh tokens do usuário ficam revogados: nenhum renova (RN-AUTH-19).
- [x] **CA-08** — A revogação cobre sessões de **outros dispositivos**, não apenas a que fez a troca.
- [x] **CA-09** — Os tokens revogados têm `RevokedReason == PasswordChanged` no banco.
- [x] **CA-10** — Sessões de **outros usuários** não são afetadas.
- [x] **CA-11** — Se a persistência do novo hash falhar, nenhuma sessão é revogada (atomicidade — testado forçando falha na transação).
- [x] **CA-12** — Se a revogação falhar, a senha **não** é alterada (mesma transação).
- [x] **CA-13** — Requisição sem autenticação retorna **401**.
- [x] **CA-14** — Nenhum log contém `currentPassword` ou `newPassword`.
- [x] **CA-15** — A resposta é **204**, sem corpo — não retorna dados do usuário nem tokens.

## Testes obrigatórios

- Unidade: `ChangePasswordHandler` — CA-04 a CA-06, CA-09.
- Integração: fluxo completo login → troca → tentativa de refresh → novo login — CA-01 a CA-03, CA-07, CA-08, CA-10, CA-13.
- Integração: atomicidade — CA-11, CA-12.

## Decisões em aberto

- **D-04** — "Esqueci minha senha" fora do escopo. Se entrar, vira task nova, não extensão desta.

## Emenda (03/10/2026) — Fase 4, onda A1

**CA-07, CA-08 e CA-09 atendidos.** `ChangePasswordHandler` chama `IRefreshTokenService.RevokeAllForUserAsync(userId, PasswordChanged)` **antes** do `SaveChangesAsync` que grava o novo hash: o hash e a revogação saem do mesmo commit. Cobertura: `ChangePasswordHandlerTests` (revoga antes de salvar; nenhuma revogação nas três falhas) e `RefreshTokenSqliteTests`/`RefreshTokenPostgresTests.ChangePassword_RevogaTodasAsSessoes` (várias sessões, motivo `PasswordChanged`; o de Postgres exige Docker e não foi executado nesta onda). **CA-11/CA-12** (atomicidade) valem por construção — o mesmo `SaveChangesAsync` —, mas não há teste que force a falha da transação; seguem abertos.

## Auditoria dos critérios (03/10/2026)

Critérios conferidos contra o código em 03/10/2026. Marcados: 12 de 15.

| CA | Situação | Evidência / motivo |
|---|---|---|
| CA-10 | em aberto | `RevokeAllForUserAsync` filtra por `UserId`, e `AccountManagementGrpcTests.ChangePassword_NaoAfetaOutroUsuario` só prova que o **login** de outro usuário continua funcionando; nenhum teste mostra o refresh token de outro usuário continuando válido após a troca. |
| CA-11 | em aberto | Atomicidade vale por construção (mesmo `SaveChangesAsync` em `ChangePasswordHandler`), mas nenhum teste força falha da transação (confirmado na emenda de 03/10). |
| CA-12 | em aberto | Idem CA-11: sem teste que force falha na revogação e confira que o hash não mudou. |
