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

- [ ] **CA-01** — `GET /api/me` autenticado retorna **200** com os dados do usuário do token.
- [ ] **CA-02** — A resposta **não** contém `passwordHash` nem qualquer campo de senha (RN-AUTH-05).
- [ ] **CA-03** — `GET /api/me` sem token retorna **401**.
- [ ] **CA-04** — Dois usuários diferentes recebem, cada um, os **próprios** dados no mesmo endpoint.
- [ ] **CA-05** — `PATCH /api/me` com `displayName` válido retorna **200**, e um `GET /api/me` subsequente reflete o novo nome (RN-USER-02).
- [ ] **CA-06** — `PATCH /api/me` com `displayName` vazio, só-espaços ou com 101 caracteres retorna **400**.
- [ ] **CA-07** — `PATCH /api/me` com `displayName` de 1 e de 100 caracteres é aceito (bordas).
- [ ] **CA-08** — O nome é salvo com trim.
- [ ] **CA-09** — Enviar `{ "displayName": "Novo", "email": "outro@x.com" }` altera **apenas** o nome; o e-mail no banco permanece inalterado (RN-USER-03).
- [ ] **CA-10** — Não existe nenhum endpoint na API que altere o e-mail de um usuário (verificado pela enumeração de rotas).
- [ ] **CA-11** — `UpdatedAt` do usuário muda após o PATCH; `CreatedAt` não.
- [ ] **CA-12** — `PATCH /api/me` sem token retorna **401**.

## Testes obrigatórios

- Integração: CA-01 a CA-09, CA-11, CA-12.
- Unidade: handler de atualização de perfil — CA-05, CA-08.
- CA-10 pode reusar a enumeração de rotas criada em BE-13.
