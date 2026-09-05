# FE-11 — Perfil do usuário

| | |
|---|---|
| **Domínio** | Usuário |
| **Depende de** | [FE-07](FE-07-roteamento-guards.md) · backend: [BE-14](../backend/BE-14-perfil-usuario.md) |
| **Bloqueia** | FE-12, FE-13 |
| **Regras cobertas** | RN-USER-01, RN-USER-02, RN-USER-03 |
| **Estimativa** | P |

## Objetivo

O usuário vê os próprios dados em `/account` e altera o nome de exibição — e a tela deixa claro que o e-mail não é alterável.

## Escopo

### Inclui

- Rota `/account`, protegida por `authGuard`, dentro do `AppShell`.
- Exibição dos dados de `GET /api/me` (RN-USER-01): e-mail, nome de exibição, data de criação da conta.
- **E-mail em modo somente leitura**, com texto explicando que ele é a identidade de login e não pode ser alterado (RN-USER-03).
- Edição do **nome de exibição** (RN-USER-02) via `PATCH /api/me`: campo obrigatório, 1–100 caracteres, com trim.
- Após salvar, o nome exibido no cabeçalho do `AppShell` é atualizado **imediatamente** — vem do mesmo `SessionStore`.
- Acessos às demais ações de conta a partir desta tela: **alterar senha** ([FE-12](FE-12-alteracao-senha.md)), **sair de todos os dispositivos** ([FE-10](FE-10-logout.md)) e **excluir conta** ([FE-13](FE-13-exclusao-conta.md)), esta última visualmente separada como ação destrutiva.
- Estados de carregando, erro (com "tentar novamente") e sucesso.

### Não inclui

- Alteração de e-mail — proibida por RN-USER-03. **Nem sequer um campo desabilitado com aparência de editável.**
- Foto/avatar, preferências, tema (fora do escopo).

## Notas técnicas

- **RN-USER-03 é uma decisão de produto, não uma limitação técnica** — a tela deve explicar isso ("o e-mail é usado para entrar e não pode ser alterado"), senão o usuário fica procurando o botão.
- O nome no cabeçalho e o nome nesta tela vêm da **mesma** fonte (`SessionStore.user`). Duplicar o estado faria o cabeçalho ficar defasado após a edição — um bug clássico e visível.
- A ação destrutiva fica separada por um divisor, com cor de perigo, longe do botão de salvar. Nunca lado a lado.
- O backend ignora um `email` enviado no corpo do PATCH ([BE-14](../backend/BE-14-perfil-usuario.md)/CA-09); ainda assim, o frontend não deve enviá-lo.

## Critérios de aceite

- [ ] **CA-01** — `/account` exibe e-mail, nome de exibição e data de criação do usuário autenticado (RN-USER-01).
- [ ] **CA-02** — A data de criação é exibida em formato legível em pt-BR.
- [ ] **CA-03** — O e-mail é exibido como **somente leitura**, com a explicação de que não pode ser alterado (RN-USER-03).
- [ ] **CA-04** — Não existe nenhum campo editável de e-mail na tela, nem desabilitado com aparência de input.
- [ ] **CA-05** — O request do `PATCH` **não** contém o campo `email` (verificado no teste da chamada).
- [ ] **CA-06** — Alterar o nome e salvar retorna sucesso e a tela reflete o novo valor (RN-USER-02).
- [ ] **CA-07** — O nome no cabeçalho do `AppShell` é atualizado **imediatamente** após salvar, sem recarregar a página.
- [ ] **CA-08** — Nome vazio, só espaços, ou com 101 caracteres é rejeitado antes do envio; 1 e 100 caracteres são aceitos.
- [ ] **CA-09** — O nome é enviado com trim.
- [ ] **CA-10** — O botão salvar fica desabilitado quando não há alteração pendente e durante o envio.
- [ ] **CA-11** — Erro na API exibe mensagem sem perder o valor digitado.
- [ ] **CA-12** — Enquanto os dados carregam, a tela exibe indicador de carregamento; se falhar, exibe erro com "tentar novamente".
- [ ] **CA-13** — A resposta exibida **não** contém nenhum campo de senha ou hash (RN-AUTH-05).
- [ ] **CA-14** — As ações "alterar senha", "sair de todos os dispositivos" e "excluir conta" estão acessíveis a partir desta tela.
- [ ] **CA-15** — "Excluir conta" está visualmente separada e marcada como ação destrutiva, longe do botão de salvar.
- [ ] **CA-16** — A tela é operável só pelo teclado, com labels associados, e usável em 360 px.

## Testes obrigatórios

- Componente (Testing Library): CA-01 a CA-12, CA-14.
- Integração com `SessionStore`: CA-07 — a sincronia do cabeçalho é o defeito mais provável desta task.
