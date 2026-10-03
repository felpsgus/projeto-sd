# ADR-0001 — O logout não invalida o access token já emitido

- **Status:** aceita (03/10/2026) — registrada como **D-41** em `tarefas/backend/DECISOES-PENDENTES.md`
- **Contexto de regra:** RN-AUTH-11, RN-AUTH-12, RN-AUTH-19; tasks BE-10 e BE-11

## Contexto

O access token é um JWT RS256 autocontido. Desde a D-38, o Gateway valida a assinatura **localmente**
com a chave pública, sem chamar o Identity a cada requisição. O refresh token, ao contrário, é opaco e
vive no banco do Identity (`identity.refresh_tokens`), então pode ser revogado na hora.

Consequência: revogar a sessão (logout, logout-all, troca de senha, exclusão de conta) derruba o refresh
token imediatamente, mas o access token emitido antes continua passando na validação até expirar.

## Decisão

Aceitar a limitação e documentá-la. Não há blocklist de `jti`, nem consulta ao Identity na borda.

- Janela máxima: `Jwt:AccessTokenMinutes` (15 por padrão, faixa 1–60).
- Depois do logout, o cliente não consegue **renovar** a sessão: `POST /api/auth/refresh` responde 401.
- O texto de RN-AUTH-12 exige invalidar o **refresh token**; não promete invalidação imediata do access token.

## Alternativas consideradas

1. **Blocklist consultada na borda.** Elimina a janela, mas reintroduz uma consulta de rede por requisição,
   exatamente o custo que a D-38 removeu, e anula a vantagem de um token autocontido.
2. **Encurtar o access token.** Reduz a janela ao custo de mais refreshes; é só configuração
   (`Jwt:AccessTokenMinutes`), então fica disponível sem nova decisão.
3. **Aceitar e documentar** (escolhida): comportamento esperado de JWT sem estado, com janela curta.

## Consequências

- Um access token roubado antes do logout ainda abre a API por até 15 minutos. O atacante não o renova.
- Operações sensíveis continuam exigindo a senha atual (troca de senha, exclusão de conta), então o access
  token sozinho não basta para assumir a conta.
- O teste `AccessTokenEmitidoAntesDoLogout_ContinuaAceitoAteExpirar` (Gateway, BE-11 CA-11) registra a
  expectativa: se a decisão mudar, o teste falha e a mudança é consciente.
- Se o requisito de revogação imediata surgir, a opção 1 vira uma nova ADR que substitui esta.
