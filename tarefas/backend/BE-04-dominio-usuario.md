# BE-04 — Domínio: entidade `User`

| | |
|---|---|
| **Domínio** | Usuário |
| **Depende de** | [BE-02](BE-02-persistencia-base.md), [BE-03](BE-03-result-erros-validacao.md) |
| **Bloqueia** | BE-06, BE-07, BE-14, BE-16 |
| **Regras cobertas** | RN-USER-01, RN-USER-03, RN-USER-04, RN-AUTH-02, RN-AUTH-03, RN-AUTH-06, RN-AUTH-07 |
| **Estimativa** | M |

## Objetivo

Existe a entidade `User` no domínio, com as suas invariantes garantidas pela própria entidade (não pelo caso de uso), persistida com unicidade de e-mail no banco.

## Escopo

### Inclui

- Entidade `User` em `Domain` com (RN-USER-01):

  | Propriedade | Tipo | Regra |
  |---|---|---|
  | `Id` | `Guid` | identificador único, gerado no domínio |
  | `Email` | value object `Email` | único, formato válido, **imutável** após criação |
  | `DisplayName` | `string` | 1–100 caracteres, editável |
  | `PasswordHash` | `string` | nunca em texto puro, nunca serializado |
  | `IsActive` | `bool` | nasce `true` |
  | `CreatedAt` | `DateTime` (UTC) | preenchido na criação |
  | `UpdatedAt` | `DateTime` (UTC) | atualizado a cada alteração |

- Value object `Email` com normalização (trim + lowercase) e validação de formato (RN-AUTH-03). Criação retorna `Result<Email>`, não lança.
- Factory `User.Create(Email, displayName?, passwordHash, TimeProvider)`:
  - se `displayName` ausente/vazio, usa a parte do e-mail antes do `@` (RN-AUTH-07);
  - `IsActive = true` (RN-AUTH-06).
- Comportamentos de domínio: `Rename(string)`, `Deactivate()`, `ChangePasswordHash(string)`. **Setters públicos são proibidos** — o estado muda por método.
- Mapeamento EF: índice **único** em `Email` (RN-AUTH-02), `Email` como conversão de/para `string`, sem setter público exposto.
- Repositório `IUserRepository` em `Application` (`GetByIdAsync`, `GetByEmailAsync`, `EmailExistsAsync`, `Add`, `Remove`) implementado em `Infrastructure`.

### Não inclui

- Regra de força de senha e hashing (BE-06).
- Endpoints (BE-07, BE-14).
- Exclusão de conta em cascata (BE-16).

## Notas técnicas

- **RN-USER-03**: `Email` não tem método de alteração. A imutabilidade é estrutural, não uma convenção.
- **RN-AUTH-05**: `PasswordHash` **NÃO DEVE** aparecer em nenhum DTO de resposta. A entidade nunca é serializada diretamente (regra de convenção 2.1).
- Comparação de e-mail é sempre sobre a forma normalizada — `João@Ex.COM` e `joao@ex.com` são o mesmo usuário.
- A unicidade tem **duas** camadas: verificação no caso de uso (mensagem amigável) **e** índice único no banco (garantia sob concorrência). A segunda não é opcional.

## Critérios de aceite

- [ ] **CA-01** — `Email.Create` rejeita: vazio, sem `@`, sem domínio, com espaços, e com mais de 254 caracteres — retornando `Result` de falha, sem lançar exceção.
- [ ] **CA-02** — `Email.Create(" João@Exemplo.COM ")` produz `joão@exemplo.com` (trim + lowercase).
- [ ] **CA-03** — Dois `Email` criados de `A@X.com` e `a@x.com` são **iguais** (`Equals` e `GetHashCode`).
- [ ] **CA-04** — `User.Create` sem `displayName` define o nome como a parte antes do `@`.
- [ ] **CA-05** — `User.Create` com `displayName` informado preserva o valor recebido (apenas com trim).
- [ ] **CA-06** — Todo usuário recém-criado tem `IsActive == true`.
- [ ] **CA-07** — A entidade `User` **não expõe nenhum setter público**; alterações só ocorrem via método de domínio (verificado por teste de arquitetura/reflection).
- [ ] **CA-08** — Não existe caminho de código que altere `Email` após a criação (não há método nem setter).
- [ ] **CA-09** — `Rename` rejeita nome vazio, só-espaços ou acima de 100 caracteres, e atualiza `UpdatedAt` quando aceita.
- [ ] **CA-10** — Inserir dois usuários com o mesmo e-mail viola o índice único do banco (teste de integração confirma a exceção do provider) — a garantia não depende apenas da checagem em memória.
- [ ] **CA-11** — Inserir dois usuários com e-mails que diferem só em maiúsculas/minúsculas também é rejeitado pelo banco.
- [ ] **CA-12** — `PasswordHash` não aparece em nenhuma serialização JSON produzida pela aplicação (verificado no teste de integração que cria e consulta um usuário).

## Testes obrigatórios

- Unidade (Domain, cobertura ≥ 85%): `Email` (CA-01 a CA-03), `User.Create` (CA-04 a CA-06), `Rename` (CA-09).
- Integração: unicidade no banco (CA-10, CA-11).
- Arquitetura/reflection: CA-07.
