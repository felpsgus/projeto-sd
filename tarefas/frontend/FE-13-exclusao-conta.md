# FE-13 — Exclusão de conta

| | |
|---|---|
| **Domínio** | Usuário |
| **Depende de** | [FE-11](FE-11-perfil-usuario.md) · backend: [BE-16](../backend/BE-16-exclusao-conta.md) |
| **Bloqueia** | — |
| **Regras cobertas** | RN-USER-05, RN-AUTH-19 |
| **Estimativa** | M |

## Objetivo

O usuário exclui a própria conta de forma deliberada, sabendo exatamente o que perde — e sem chance de fazê-lo por acidente.

## Escopo

### Inclui

- Ação "Excluir minha conta" em `/account`, na área de ações destrutivas.
- **Diálogo de confirmação** (`<app-confirm-dialog>` de [FE-04](FE-04-layout-design-base.md)) contendo:
  - aviso de que a ação é **permanente e irreversível**;
  - que **todas as tarefas serão apagadas** (RN-USER-05), com a quantidade atual quando conhecida;
  - que todas as sessões serão encerradas (RN-AUTH-19);
  - campo de **confirmação de senha** (D-19 do backend);
  - botão de confirmação em cor de perigo, **não** sendo o botão padrão do diálogo.
- Envio a `DELETE /api/me` com a senha no corpo. Sucesso (204):
  1. encerra a sessão local com motivo `account_deleted`;
  2. leva a `/login` com mensagem de conta excluída.
- Erro de senha incorreta → mensagem dentro do diálogo, que **permanece aberto**; nada é excluído.

### Não inclui

- Exportação de dados antes da exclusão.
- Período de arrependimento ou desativação temporária — [BE-16](../backend/BE-16-exclusao-conta.md) adota exclusão imediata (D-05).

## Notas técnicas

- **Esta é a única ação verdadeiramente irreversível da aplicação.** As três barreiras — diálogo explícito, senha, botão não-padrão — são proporcionais a isso, não excesso de zelo. Um clique acidental aqui apaga tudo que o usuário construiu.
- **Não usar `confirm()` do navegador:** não é estilizável, não recebe campo de senha e tem comportamento inconsistente com leitor de tela.
- O botão de confirmar **não** deve receber foco inicial nem responder a `Enter` — o foco inicial vai para o campo de senha. `Esc` cancela.
- A contagem de tarefas exibida no aviso vem do estado já carregado ([FE-14](FE-14-servico-estado-tarefas.md)); se não estiver disponível, usar texto sem número em vez de disparar uma chamada só para isso.
- Após o sucesso, **não** chamar `/api/auth/logout`: a conta não existe mais e a chamada retornaria 401.

## Critérios de aceite

### Confirmação

- [ ] **CA-01** — A ação abre um diálogo; **nada** é excluído com um único clique.
- [ ] **CA-02** — O diálogo informa que a exclusão é permanente e que todas as tarefas serão apagadas (RN-USER-05).
- [ ] **CA-03** — O diálogo informa que todas as sessões serão encerradas (RN-AUTH-19).
- [ ] **CA-04** — O diálogo exige a senha; sem preenchê-la, o botão de confirmar fica desabilitado.
- [ ] **CA-05** — O foco inicial do diálogo vai para o campo de senha, **não** para o botão de confirmar.
- [ ] **CA-06** — `Enter` no campo de senha **não** confirma a exclusão diretamente sem que o botão esteja habilitado e acionado.
- [ ] **CA-07** — `Esc` e o botão cancelar fecham o diálogo sem excluir nada, devolvendo o foco à ação de origem.
- [ ] **CA-08** — O botão de confirmar usa cor de perigo e texto explícito ("Excluir permanentemente"), não "OK".
- [ ] **CA-09** — O diálogo prende o foco enquanto aberto.

### Fluxo

- [ ] **CA-10** — Confirmação com senha correta chama `DELETE /api/me` e retorna sucesso.
- [ ] **CA-11** — Após o sucesso, o usuário está em `/login` com mensagem de conta excluída.
- [ ] **CA-12** — Após o sucesso, nenhum token permanece em memória ou storage.
- [ ] **CA-13** — Após o sucesso, tentar entrar com as antigas credenciais falha com a mensagem genérica de credencial inválida (RN-AUTH-09).
- [ ] **CA-14** — Após o sucesso, **nenhuma** chamada a `/api/auth/logout` é feita.
- [ ] **CA-15** — O estado de tarefas em memória é limpo.

### Erros

- [ ] **CA-16** — Senha incorreta exibe o erro **dentro do diálogo**, que permanece aberto, e nada é excluído.
- [ ] **CA-17** — Após esse erro, corrigir a senha e confirmar funciona sem fechar e reabrir o diálogo.
- [ ] **CA-18** — Erro de rede exibe mensagem e a conta permanece intacta; o usuário continua autenticado.
- [ ] **CA-19** — O botão de confirmar fica desabilitado durante a requisição; clique duplo dispara **uma** chamada.

### Segurança

- [ ] **CA-20** — A senha digitada não aparece em storage, URL, `console` ou atributo do DOM.
- [ ] **CA-21** — O campo de senha do diálogo usa `type="password"` e `autocomplete="current-password"`.

## Testes obrigatórios

- Componente (Testing Library): CA-01 a CA-19 — a maior parte do valor desta task está em provar que **não** se exclui por acidente.
- **CA-05, CA-06 e CA-07 são obrigatórios**: são o que separa uma confirmação real de um obstáculo cosmético.
- **CA-20 é teste de segurança obrigatório.**
