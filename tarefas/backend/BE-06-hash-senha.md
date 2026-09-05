# BE-06 — Hashing de senha e política de senha

| | |
|---|---|
| **Domínio** | Autenticação |
| **Depende de** | [BE-04](BE-04-dominio-usuario.md) |
| **Bloqueia** | BE-07, BE-09, BE-15 |
| **Regras cobertas** | RN-AUTH-04, RN-AUTH-05 |
| **Estimativa** | P |

## Objetivo

Senha nunca existe em texto puro fora do momento da requisição: há um serviço de hash com algoritmo moderno e uma política de força de senha reaproveitável por cadastro e troca de senha.

## Escopo

### Inclui

- Abstração `IPasswordHasher` em `Application`:
  - `string Hash(string plainPassword)`
  - `bool Verify(string plainPassword, string hash)`
- Implementação em `Infrastructure` com algoritmo de derivação lento e salt por senha: **Argon2id** (preferido) ou **PBKDF2-SHA256** com custo alto. Parâmetros de custo em `IOptions<PasswordHashingOptions>`.
- O hash gerado **DEVE** ser auto-descritivo (embute algoritmo, parâmetros e salt), permitindo evoluir o custo sem quebrar hashes existentes.
- Value object / validador de política de senha (RN-AUTH-04), reutilizável:
  - mínimo **8 caracteres**;
  - ao menos **uma letra**;
  - ao menos **um número**.
  Retorna `Result` com **todos** os problemas encontrados, não só o primeiro.
- Regra de log: senha em texto puro e hash **NUNCA** entram em log, exceção, `ToString()` ou resposta HTTP.

### Não inclui

- Endpoints de cadastro/login/troca (BE-07, BE-09, BE-15).
- Bloqueio por tentativas (BE-12).

## Notas técnicas

- `Verify` **DEVE** usar comparação em tempo constante.
- Não implementar criptografia própria — usar biblioteca estabelecida (`Konscious.Security.Cryptography.Argon2` ou `Rfc2898DeriveBytes` do BCL).
- O custo de hashing deixa o login propositalmente lento (~50–250 ms). Os testes de integração **DEVEM** usar parâmetros de custo reduzidos via configuração de teste, nunca desativar o hashing.
- A política é validada na **borda** (FluentValidation no request), mas o validador vive em `Application` para ser reusado por BE-07 e BE-15 sem duplicação.

## Critérios de aceite

- [ ] **CA-01** — `Hash("senha")` chamado duas vezes produz **hashes diferentes** (salt por senha).
- [ ] **CA-02** — `Verify(senhaCorreta, hash)` é `true`; `Verify(senhaErrada, hash)` é `false`.
- [ ] **CA-03** — `Verify` com hash malformado retorna `false` **sem lançar exceção**.
- [ ] **CA-04** — O hash produzido não contém a senha em nenhuma forma recuperável (não é Base64/hex da senha, não é hash rápido sem salt).
- [ ] **CA-05** — Mudar o parâmetro de custo na configuração **não invalida** hashes já gerados: `Verify` continua aceitando senhas antigas.
- [ ] **CA-06** — A política aceita: `"abc12345"`, `"Senha123"`. Rejeita: `"abc1234"` (7 caracteres), `"abcdefgh"` (sem número), `"12345678"` (sem letra), `""` e `null`.
- [ ] **CA-07** — Uma senha que viola duas regras retorna **duas** mensagens, não uma.
- [ ] **CA-08** — Nenhum tipo do fluxo de senha expõe a senha em `ToString()` (verificado por teste).
- [ ] **CA-09** — Uma busca no repositório por logs/serialização confirma que nem a senha nem o hash aparecem em saída de log em nenhum nível, inclusive `Debug`.
- [ ] **CA-10** — O tempo de `Hash` com os parâmetros de produção está na faixa alvo (medido e documentado no PR; não é assert de teste, para não ficar flaky).

## Testes obrigatórios

- Unidade: CA-01 a CA-08 (parametrizados para a política).
- Os testes usam parâmetros de custo reduzidos, configurados — nunca um `IPasswordHasher` fake que não faz hash.
