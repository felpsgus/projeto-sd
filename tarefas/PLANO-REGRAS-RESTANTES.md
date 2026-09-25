# Plano — fechar as regras de negócio que faltam

> Escrito em 23/09/2026, depois da Etapa 11 do T2. Este documento cobre **só o que sobra do produto**:
> nenhuma das tarefas abaixo é exigida pelo T2 (22/10) ou pelo T3 (03/12).
> A quebra por task continua em [backend/](backend/) e [frontend/](frontend/); aqui está a **ordem**,
> o que cada fase fecha, e as decisões que precisam ser tomadas antes de codar.

## Situação em 23/09/2026

Das 54 regras de [REGRAS-DE-NEGOCIO.md](../REGRAS-DE-NEGOCIO.md):

| Estado | Quantidade |
|---|---|
| Cumpridas | 24 |
| Parciais (regra no domínio, falta caso de uso ou rota) | 10 |
| Não iniciadas | 19 |
| Fora do escopo (D-04, "esqueci minha senha") | 1 |

Sobram **25 tasks**: 14 de backend (BE-07, 09, 10, 11, 12, 14, 15, 16, 19, 20, 21, 22, 23, 24) e
11 de frontend (FE-08, 11, 12, 13, 16, 18, 19, 20, 21, 22, 23). Três são grandes: BE-10, BE-22 e FE-16.

---

## Decisões a tomar antes de começar

Três consequências do que foi construído no T2 mudam o desenho original de algumas tasks. Registre-as
em [backend/DECISOES-PENDENTES.md](backend/DECISOES-PENDENTES.md) antes da fase correspondente.

### 1. O logout não invalida o access token (afeta a Fase 4)

Desde a BE-40/D-38, o Gateway valida a assinatura do JWT **localmente** e não consulta o Identity a
cada requisição. Revogar uma sessão (BE-11) derruba o refresh token, mas o access token continua
válido até expirar — hoje, até 15 minutos.

Opções:

1. **Aceitar e documentar** (recomendado): é o comportamento esperado de JWT sem estado; o custo é
   uma janela curta em que um token já "deslogado" ainda funciona.
2. Encurtar a vida do access token: reduz a janela, aumenta a frequência de refresh.
3. Reintroduzir checagem de revogação na borda: elimina a janela e traz de volta exatamente o custo de
   rede por requisição que a BE-40 removeu.

A escolha muda os critérios de aceite de BE-11 e o texto de RN-AUTH-12.

### 2. ~~Excluir a conta cruza dois serviços~~ — decisão inexistente (corrigido em 24/09/2026)

**Este item estava errado.** Ao começar a Fase 3, a leitura de [BE-16](backend/BE-16-exclusao-conta.md) mostrou
que o problema já está resolvido desde o T1: a FK `tasks.tasks.owner_id → identity.users(id)` tem
**`ON DELETE CASCADE`** (migration `AddOwnerForeignKeyToIdentityUsers`, BE-02 CA-02c). Apagar o usuário
apaga as tarefas de forma atômica, **sem RPC novo e sem orquestração no Gateway** — inclusive as que
estavam com soft delete, porque a cascata opera sobre linhas, não sobre o filtro de query do EF.
É o ganho concreto de manter um banco só (D-27). O texto original abaixo fica como registro do erro.

### 2. (texto original, superado) Excluir a conta cruza dois serviços

RN-USER-05 exige que excluir a conta remova as tarefas. Quem guarda tarefa é o Tasks; quem guarda
usuário é o Identity. Hoje a dependência é unidirecional: Tasks → Identity (`ValidateUser`).

Opções:

1. **Gateway orquestra** (recomendado): remove as tarefas via gRPC no Tasks e, só então, a conta no
   Identity. Mantém a direção das dependências. Precisa definir o que acontece se o segundo passo
   falhar — a escolha natural é a ordem inversa da ingenuidade: desativar a conta primeiro, apagar as
   tarefas depois, de forma que uma falha no meio nunca deixe tarefas órfãs acessíveis.
2. Identity chama o Tasks: cria dependência circular entre os serviços.
3. Evento assíncrono: correto a longo prazo, e desproporcional para a escala deste projeto.

### 3. A sessão do frontend volta ao desenho original (afeta a Fase 4)

A **FD-20** (access token só em memória) existe porque o T2 não tem refresh token (D-36). Quando a
BE-10 entrar, valem de novo a **FD-01** (refresh em cookie `HttpOnly`) e a FD-16 — que a Etapa 11 já
satisfez de verdade, com o nginx servindo front e API na mesma origem. Na prática, **a FD-20 é
revogada** nessa hora, e FE-05, FE-06 e FE-10 voltam ao escopo integral.

---

## Fase 1 — Ciclo de vida da tarefa

**Tasks:** [BE-20](backend/BE-20-concluir-reabrir-tarefa.md), [BE-19](backend/BE-19-editar-tarefa.md),
[BE-21](backend/BE-21-remover-tarefa.md) · [FE-18](frontend/FE-18-editar-tarefa.md),
[FE-19](frontend/FE-19-concluir-reabrir.md), [FE-20](frontend/FE-20-remover-tarefa.md)

**Fecha:** RN-TASK-08, RN-TASK-09, RN-TASK-11, RN-TASK-12, RN-TASK-13 (cinco regras, três hoje parciais).

**Por que primeiro.** É a fase com a melhor relação entre esforço e resultado: a entidade `TodoTask` já
tem `Complete`, `Reopen` e o soft delete desde o T1 — falta só o caminho até a tela. E é o que dá ao
avaliador a **alteração** no banco: hoje a demonstração mostra inserção e consulta, nunca um `UPDATE`.

**O que envolve:**

- handlers em Application, reaproveitando o comportamento que já existe no domínio;
- RPCs novos em `tasks.proto`: `UpdateTask`, `CompleteTask`, `ReopenTask`, `DeleteTask` — extensão
  aditiva da v1, como foi a BE-41;
- rotas no Gateway, com os verbos adequados e o mapeamento de erro que já existe (D-35);
- na UI, as ações na própria linha da lista, sem tela nova para concluir e remover;
- confirmação antes de remover, e o soft delete continuando invisível para o usuário.

## Fase 2 — Listagem completa

**Tasks:** [BE-22](backend/BE-22-listagem-tarefas.md) (G) · [FE-16](frontend/FE-16-filtros-busca-url.md) (G)

**Fecha:** RN-LIST-02, 03, 04, 05, 06.

A BE-41 deixou pronta a paginação, os limites (`PagingOptions`, D-09) e o contrato. Aqui entram filtro
por estado, prioridade e atraso, busca textual e a ordenação completa de RN-LIST-06, que **substitui** o
critério provisório da BE-41 (criação decrescente), não convive com ele.

No frontend, os filtros sincronizam com a URL — é o que torna um resultado compartilhável e o botão
"voltar" previsível.

## Fase 3 — Cadastro e conta

**Tasks:** [BE-07](backend/BE-07-cadastro-usuario.md), [BE-14](backend/BE-14-perfil-usuario.md),
[BE-15](backend/BE-15-alteracao-senha.md), [BE-16](backend/BE-16-exclusao-conta.md) ·
[FE-08](frontend/FE-08-tela-cadastro.md), [FE-11](frontend/FE-11-perfil-usuario.md),
[FE-12](frontend/FE-12-alteracao-senha.md), [FE-13](frontend/FE-13-exclusao-conta.md)

**Fecha:** RN-AUTH-01, 02, 03, 06, 07, 21 · RN-USER-02, 03, 05.

**Efeito colateral bom:** com cadastro real, o seed de demonstração perde a razão de existir.
`UserStore:SeedDemoUsers` e `UserStore:DemoUserPassword` saem de cena, e com eles o aviso de "nunca
ligue isto em produção" que hoje mora no `identity.env.example`.

**Não depende de decisão nenhuma.** A "decisão 2" que esta linha citava não existia — ver a correção
acima: a exclusão de conta sai de graça pela cascata da FK.

**Decisão tomada durante a fase (24/09/2026): senha atual errada responde 400, não 401.** A BE-15
deixava o status aberto ("400/401"). Vale 400 porque a requisição **está autenticada** — o que falhou é
um campo do corpo, não a credencial que autentica a chamada — e porque o interceptor do frontend
(FE-06) trata todo 401 como sessão expirada e redireciona ao login, o que expulsaria o usuário do app
no meio do formulário de troca de senha. O `error-code` continua `auth.invalid_current_password`
(exigência do CA-04), e a mensagem viaja no trailer de erros por campo. O `auth.user_not_found`
**segue** 401, de propósito: uma sessão cujo usuário não existe mais é indistinguível de sessão
inválida (BE-16 CA-09).

**Ordem de execução em ondas:** Identity → Gateway → frontend. O seed de demonstração só é removido no
**fim** da fase, depois que o cadastro funcionar pelo frontend — tirá-lo antes deixaria o projeto sem
como entrar no app.

## Fase 4 — Sessão completa

**Tasks:** [BE-09](backend/BE-09-login.md) (o que sobrou de D-36), [BE-10](backend/BE-10-refresh-token-rotacao.md) (G),
[BE-11](backend/BE-11-logout-revogacao.md), [BE-12](backend/BE-12-bloqueio-tentativas-login.md) ·
FE-05, FE-06 e FE-10 revisitadas

**Fecha:** RN-AUTH-10, 12, 13, 14, 15, 16, 17, 18, 19, 20.

É a fase mais pesada, e a única que **reabre código já entregue e verificado** — o interceptor do
frontend, a sessão em memória, os endpoints de autenticação do Gateway. Por isso vem depois: enquanto
as fases 1 a 3 ainda mudam a superfície da API, mexer na sessão é retrabalho garantido.

**Depende das decisões 1 e 3.**

Pontos de atenção:

- rotação com detecção de reuso (RN-AUTH-17) exige guardar a família do token, não só o token;
- o cookie `HttpOnly` só funciona de forma limpa na mesma origem — o que o nginx da Etapa 11 garante;
- o bloqueio por tentativas (BE-12) precisa de onde contar as falhas, e essa contagem é estado novo
  no Identity;
- trocar a senha e excluir a conta revogam sessões (RN-AUTH-19), o que liga a Fase 3 a esta.

## Fase 5 — Operação e qualidade

**Tasks:** [BE-23](backend/BE-23-expurgo-tarefas-removidas.md), [BE-24](backend/BE-24-observabilidade-ci.md) ·
[FE-21](frontend/FE-21-acessibilidade-responsividade.md), [FE-22](frontend/FE-22-testes-e2e.md),
[FE-23](frontend/FE-23-ci-build-seguranca.md)

Fecha o que resta de RN-TASK-13 (retenção do soft delete) e o que é qualidade, não regra: expurgo,
observabilidade, gate de cobertura, CI, acessibilidade e testes de ponta a ponta com Playwright.

---

## Quando executar

Nenhuma fase é exigida pelas entregas da disciplina. A recomendação:

1. **Até 22/10** — nada disto. A prioridade é a VM do T2 e o ensaio cronometrado.
2. **Entre 22/10 e 03/12** — nada disto, salvo se sobrar folga. A prioridade é o T3.
3. **Se sobrar folga antes da apresentação** — só a **Fase 1**, que é barata e fortalece a
   demonstração de persistência real que o enunciado do T2 cobra, mostrando `UPDATE` além de `INSERT`.
4. **Depois de 03/12** — Fases 2 a 5, na ordem acima.

## Esforço

| Tamanho | Backend | Frontend |
|---|---|---|
| Grande | BE-10, BE-22 | FE-16 |
| Médio | BE-07, 09, 12, 15, 16, 19, 20, 21, 24 | FE-08, 12, 13, 18, 19, 21, 22, 23 |
| Pequeno | BE-11, 14, 23 | FE-11, 20 |

Somando, é mais trabalho do que todo o T2 que já foi feito. As fases 1 e 2 sozinhas fecham metade das
regras que faltam.
