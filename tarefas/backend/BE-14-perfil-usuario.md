# BE-14 — Perfil: consultar e editar nome de exibição

| | |
|---|---|
| **Domínio** | Usuário |
| **Depende de** | [BE-04](BE-04-dominio-usuario.md), [BE-13](BE-13-protecao-endpoints.md) |
| **Bloqueia** | — |
| **Regras cobertas** | RN-USER-02, RN-USER-03 |
| **Estimativa** | P |

## Objetivo

O usuário autenticado vê os próprios dados e altera o nome de exibição — e não consegue alterar o e-mail.

## Escopo

### Inclui

- **`GET /api/me`** (autenticado) → **200**:

  ```json
  { "id": "guid", "email": "string", "displayName": "string", "createdAt": "iso-8601" }
  ```

- **`PATCH /api/me`** (autenticado), request `{ "displayName": "string" }` → **200** com o perfil atualizado.
- Validação de `displayName`: obrigatório no PATCH, 1–100 caracteres após trim, não pode ser só espaços.
- O caso de uso opera **sempre** sobre `ICurrentUser.Id` — não existe parâmetro de rota com id de usuário, então não há como atingir outro perfil.

### Não inclui

- Alteração de e-mail — proibida por RN-USER-03.
- Troca de senha (BE-15) e exclusão de conta (BE-16).
- Foto/avatar, preferências (fora do escopo).

## Notas técnicas

- **RN-USER-03 é garantida por construção**: o DTO de request do PATCH **não tem** campo `email`. Se alguém enviar `email` no corpo, ele é simplesmente ignorado pela desserialização — e há um teste provando isso.
- Não existe `GET /api/users/{id}`. A ausência do endpoint é intencional: sem ele, não há superfície para enumerar usuários.
- A alteração atualiza `UpdatedAt` via `User.Rename` (BE-04).

## Critérios de aceite

- [x] **CA-01** — `GET /api/me` autenticado retorna **200** com os dados do usuário do token.
- [x] **CA-02** — A resposta **não** contém `passwordHash` nem qualquer campo de senha (RN-AUTH-05).
- [x] **CA-03** — `GET /api/me` sem token retorna **401**.
- [x] **CA-04** — Dois usuários diferentes recebem, cada um, os **próprios** dados no mesmo endpoint.
- [x] **CA-05** — `PATCH /api/me` com `displayName` válido retorna **200**, e um `GET /api/me` subsequente reflete o novo nome (RN-USER-02).
- [x] **CA-06** — `PATCH /api/me` com `displayName` vazio, só-espaços ou com 101 caracteres retorna **400**.
- [x] **CA-07** — `PATCH /api/me` com `displayName` de 1 e de 100 caracteres é aceito (bordas).
- [x] **CA-08** — O nome é salvo com trim.
- [x] **CA-09** — Enviar `{ "displayName": "Novo", "email": "outro@x.com" }` altera **apenas** o nome; o e-mail no banco permanece inalterado (RN-USER-03).
- [ ] **CA-10** — Não existe nenhum endpoint na API que altere o e-mail de um usuário (verificado pela enumeração de rotas).
- [x] **CA-11** — `UpdatedAt` do usuário muda após o PATCH; `CreatedAt` não.
- [x] **CA-12** — `PATCH /api/me` sem token retorna **401**.

## Testes obrigatórios

- Integração: CA-01 a CA-09, CA-11, CA-12.
- Unidade: handler de atualização de perfil — CA-05, CA-08.
- CA-10 pode reusar a enumeração de rotas criada em BE-13.

## Auditoria dos critérios (03/10/2026)

Critérios conferidos contra o código em 03/10/2026. Marcados: 11 de 12.

| CA | Situação | Evidência / motivo |
|---|---|---|
| CA-10 | em aberto | Nenhuma rota altera e-mail (`UpdateProfileHttpRequest`/`UpdateProfileRequest` não têm o campo; `UserArchitectureTests.User_NaoTemNenhumMetodoPublicoQueAlterEEmail` guarda o domínio), mas não existe teste que enumere as rotas do Gateway e afirme a ausência de rota de e-mail (`RouteGuardTests` só verifica anonimato). Lacuna de teste. |
