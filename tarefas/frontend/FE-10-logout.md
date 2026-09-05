# FE-10 — Logout e encerramento de sessão

| | |
|---|---|
| **Domínio** | Autenticação |
| **Depende de** | [FE-09](FE-09-tela-login.md) · backend: [BE-11](../backend/BE-11-logout-revogacao.md) |
| **Bloqueia** | — |
| **Regras cobertas** | RN-AUTH-12, RN-AUTH-19 |
| **Estimativa** | P |

## Objetivo

O usuário sai da aplicação quando quiser, e nada do que ele viu permanece acessível na aba depois disso.

## Escopo

### Inclui

- Ação **"Sair"** no menu de conta do `AppShell` ([FE-04](FE-04-layout-design-base.md)).
- Ação **"Sair de todos os dispositivos"** na tela de perfil ([FE-11](FE-11-perfil-usuario.md)), chamando `POST /api/auth/logout-all` (RN-AUTH-19).
- Fluxo do logout:
  1. chama `POST /api/auth/logout` com **corpo vazio** e `withCredentials: true` — a sessão é identificada pelo cookie (FD-01), e é o backend quem o apaga na resposta;
  2. executa `SessionStore.endSession('user_logout')` — limpa tokens, usuário e **estado de features em memória**;
  3. navega para `/login`, substituindo o histórico (`replaceUrl`), para o botão "voltar" não retornar à área autenticada;
  4. exibe confirmação discreta de que a sessão foi encerrada.
- **A sessão local é encerrada mesmo se a chamada à API falhar** — rede caída não pode prender o usuário logado na aba.
- Limpeza do estado de tarefas ([FE-14](FE-14-servico-estado-tarefas.md)) e de qualquer cache em memória.

### Não inclui

- Encerramento por expiração ou revogação — é [FE-06](FE-06-interceptor-auth-refresh.md).
- Timeout por inatividade — não consta das regras de negócio.

## Notas técnicas

- **A limitação herdada de [BE-11](../backend/BE-11-logout-revogacao.md) vale aqui:** o access token já emitido continua criptograficamente válido por até 15 minutos após o logout. Como o frontend o descarta da memória, ninguém na aba consegue usá-lo — mas isso é uma propriedade do cliente, não uma garantia do sistema. Não prometer ao usuário mais do que o sistema entrega.
- **`replaceUrl` no redirecionamento é o detalhe que costuma faltar.** Sem ele, "voltar" renderiza a última tela autenticada a partir do cache do roteador, exibindo dados do usuário anterior por alguns instantes — em máquina compartilhada isso é um vazamento real.
- Encerrar a sessão mesmo com a API fora do ar é a escolha certa: o refresh token continuará válido no servidor até expirar, mas manter o usuário logado contra a vontade dele é pior.
- **Com o cookie (FD-01), a falha da API tem uma consequência a mais:** se `POST /api/auth/logout` não chega ao servidor, o cookie **não é apagado** — o frontend não consegue apagá-lo sozinho, porque é `HttpOnly`. A sessão local cai, o usuário vai para o login, mas o cookie continua no navegador até expirar. Isso não é vazamento (o token segue válido no servidor de qualquer forma, mesmo cenário de antes), e o próximo login o sobrescreve. Vale conhecer para não diagnosticar errado.
- O logout não pede confirmação — é reversível (basta entrar de novo) e a fricção não se justifica.

## Critérios de aceite

- [ ] **CA-01** — A ação "Sair" está no menu de conta e é alcançável pelo teclado.
- [ ] **CA-02** — Sair chama `POST /api/auth/logout` com **corpo vazio** e `withCredentials: true`; nenhum token é enviado pelo frontend (RN-AUTH-12, FD-01).
- [ ] **CA-02b** — Após o logout bem-sucedido, `document.cookie` não contém mais o cookie de sessão — apagado pela resposta do backend.
- [ ] **CA-03** — Após sair, o usuário está em `/login` e o `AppShell` não é mais exibido.
- [ ] **CA-04** — Após sair, nenhum token permanece em memória ou storage (teste que inspeciona ambos).
- [ ] **CA-05** — Após sair, acessar `/tasks` pela URL leva ao login (o guard de [FE-07](FE-07-roteamento-guards.md) atua).
- [ ] **CA-06** — O botão "voltar" do navegador **não** exibe a tela autenticada anterior, nem por um instante (`replaceUrl`).
- [ ] **CA-07** — O estado de tarefas em memória é limpo: entrar com **outro** usuário na mesma aba não mostra nenhum dado do anterior.
- [ ] **CA-08** — Se `POST /api/auth/logout` falhar (500 ou rede fora), a sessão local **ainda assim** é encerrada e o usuário vai para o login.
- [ ] **CA-09** — Nesse caso, o erro da API não é exibido como falha da operação — o usuário saiu, do ponto de vista dele.
- [ ] **CA-10** — "Sair de todos os dispositivos" chama `POST /api/auth/logout-all` e também encerra a sessão local (RN-AUTH-19).
- [ ] **CA-11** — Após o logout-all, uma segunda aba aberta com o mesmo usuário também é encerrada (sincronia entre abas de [FE-05](FE-05-estado-sessao.md)).
- [ ] **CA-12** — O logout não exibe diálogo de confirmação.
- [ ] **CA-13** — Nenhuma requisição pendente exibe toast de erro após o redirecionamento.

## Testes obrigatórios

- Componente/integração: CA-01 a CA-10, CA-13.
- **CA-04 e CA-07 são testes de segurança obrigatórios** — o vazamento entre usuários na mesma aba é silencioso e não aparece em revisão de código.
- Logout entra no E2E crítico de [FE-22](FE-22-testes-e2e.md), incluindo o teste do botão "voltar" (CA-06).
