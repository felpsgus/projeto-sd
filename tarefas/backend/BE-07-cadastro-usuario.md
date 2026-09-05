# BE-07 — Cadastro de usuário

| | |
|---|---|
| **Domínio** | Autenticação |
| **Depende de** | [BE-04](BE-04-dominio-usuario.md), [BE-06](BE-06-hash-senha.md) |
| **Bloqueia** | BE-09 |
| **Regras cobertas** | RN-AUTH-01, RN-AUTH-02, RN-AUTH-03, RN-AUTH-04, RN-AUTH-05, RN-AUTH-06, RN-AUTH-07 |
| **Estimativa** | M |

## Objetivo

Um visitante consegue criar a própria conta com e-mail e senha, e a conta nasce ativa e apta a autenticar.

## Escopo

### Inclui

- Endpoint **`POST /api/auth/register`**, **anônimo** (RN-AUTH-01 / D-01).
- Request:

  ```json
  { "email": "string", "password": "string", "displayName": "string|null" }
  ```

- Caso de uso `RegisterUserHandler` em `Application`:
  1. valida o request (FluentValidation: e-mail com formato válido, senha pela política de BE-06);
  2. normaliza o e-mail e verifica duplicidade → `Conflict` se existir (RN-AUTH-02);
  3. gera o hash da senha (BE-06);
  4. cria o `User` via factory de domínio (nome padrão vindo do e-mail, ativo — RN-AUTH-06/07);
  5. persiste.
- Response **201 Created** com:

  ```json
  { "id": "guid", "email": "string", "displayName": "string", "createdAt": "iso-8601" }
  ```

- Tratamento de corrida: se o índice único do banco violar apesar da checagem prévia, o handler converte a violação em `Conflict` — o cliente nunca vê 500.
- Documentação OpenAPI do endpoint, com os códigos de resposta e exemplos.

### Não inclui

- Login e emissão de tokens (BE-08, BE-09) — o cadastro **não** autentica automaticamente nesta versão.
- Confirmação de e-mail, convite, aprovação (fora do escopo, seção 8).

## Notas técnicas

- **RN-AUTH-05**: a resposta **não** contém senha nem hash. O DTO de resposta não tem esse campo — a garantia é estrutural.
- O request **não** é logado. Se houver log de requisição, o corpo de `/api/auth/register` é redigido.
- Mensagem de conflito de e-mail: aqui o vazamento de existência é aceitável e desejável (o usuário precisa saber que já tem conta). Isso **não** contradiz RN-AUTH-09, que fala de *login*.
- `displayName` ausente, `null` ou só-espaços recebe o mesmo tratamento: usa a parte antes do `@`.

## Critérios de aceite

- [ ] **CA-01** — `POST /api/auth/register` com dados válidos retorna **201** e o corpo contém `id`, `email` (normalizado), `displayName` e `createdAt`.
- [ ] **CA-02** — O usuário criado existe no banco com `IsActive == true` e `PasswordHash` preenchido (RN-AUTH-06).
- [ ] **CA-03** — O `PasswordHash` gravado **não** é igual à senha enviada.
- [ ] **CA-04** — A resposta **não contém** nenhum campo de senha ou hash, em nenhum cenário (sucesso ou erro).
- [ ] **CA-05** — Cadastrar um e-mail já existente retorna **409** com código de erro estável (ex.: `auth.email_already_registered`).
- [ ] **CA-06** — Cadastrar `JOAO@Exemplo.com` quando já existe `joao@exemplo.com` também retorna **409** (RN-AUTH-02, case-insensitive).
- [ ] **CA-07** — E-mail com formato inválido retorna **400** apontando o campo `email` (RN-AUTH-03).
- [ ] **CA-08** — Senha fora da política retorna **400** apontando o campo `password`, com a lista de violações (RN-AUTH-04).
- [ ] **CA-09** — Cadastro sem `displayName` produz o nome igual à parte antes do `@` (RN-AUTH-07) — verificado na resposta e no banco.
- [ ] **CA-10** — Cadastro com `displayName: "   "` recebe o mesmo tratamento de ausente.
- [ ] **CA-11** — Cadastro com `displayName` válido preserva o valor informado.
- [ ] **CA-12** — Duas requisições concorrentes com o mesmo e-mail resultam em **exatamente um** usuário criado; a outra recebe **409**, nunca 500 (teste de integração com execução paralela).
- [ ] **CA-13** — O endpoint é acessível **sem** token de autenticação.
- [ ] **CA-14** — Nenhum log gerado durante o cadastro contém a senha enviada.
- [ ] **CA-15** — O endpoint aparece na especificação OpenAPI com os status 201, 400 e 409 documentados.

## Testes obrigatórios

- Unidade: `RegisterUserHandler` com `IUserRepository` e `IPasswordHasher` substituídos (NSubstitute) — CA-02, CA-05, CA-09 a CA-11.
- Unidade: validador do request — CA-07, CA-08.
- Integração (`WebApplicationFactory` + Testcontainers): CA-01, CA-03 a CA-06, CA-12, CA-13.

## Decisões em aberto

- **D-01** — Auto-cadastro liberado a qualquer visitante. Padrão adotado: sim.
