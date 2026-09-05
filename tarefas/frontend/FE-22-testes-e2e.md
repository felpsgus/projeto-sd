# FE-22 — Testes E2E com Playwright

| | |
|---|---|
| **Domínio** | Qualidade |
| **Depende de** | FE-08 a FE-20 · backend: [BE-24](../backend/BE-24-observabilidade-ci.md) |
| **Bloqueia** | — |
| **Regras cobertas** | valida ponta a ponta as regras já implementadas |
| **Estimativa** | M |

## Objetivo

Os fluxos que, se quebrarem, inutilizam o produto estão cobertos por testes que exercitam frontend e backend reais.

## Escopo

### Inclui

- **Playwright** configurado: navegadores da matriz de suporte, um viewport desktop e um mobile (360 px).
- Ambiente de teste: frontend servido em modo de produção + backend real + banco em container, subidos por um comando único.
- **Isolamento**: cada teste cria o próprio usuário (e-mail único gerado) e os próprios dados. Nenhum teste depende de estado deixado por outro nem de ordem de execução.
- **Fluxos críticos cobertos** (e apenas eles — convenção 4.3: E2E não cobre cada tela):

  | # | Fluxo | Regras exercitadas |
  |---|---|---|
  | 1 | Cadastro → login → chega na lista vazia | RN-AUTH-01, 06, 08 |
  | 2 | Criar tarefa → aparece na lista → concluir → aparece como concluída | RN-TASK-10, 07, 08 |
  | 3 | Editar tarefa → alteração persiste após recarregar | RN-TASK-11, 14 |
  | 4 | Remover tarefa com confirmação → some da lista e do recarregamento | RN-TASK-12, 13 |
  | 5 | Filtrar e buscar → URL reflete → `F5` preserva a visão | RN-LIST-02 a 05, 07 |
  | 6 | Login com senha errada → mensagem genérica | RN-AUTH-09 |
  | 7 | Trocar senha → sessão cai → login com a nova senha funciona | RN-AUTH-21, 19 |
  | 8 | Sessão persiste ao recarregar (`F5`) numa rota autenticada | RN-AUTH-14 |
  | 9 | Expiração do access token → renovação transparente, sem interrupção | RN-AUTH-11, 14, 16 |
  | 10 | Logout → `/tasks` pela URL leva ao login; "voltar" não mostra a tela anterior | RN-AUTH-12, RN-AUTZ-04 |
  | 11 | Usuário A não acessa tarefa de B: `/tasks/{id_de_B}/edit` exibe "não encontrada" | RN-AUTZ-02, 03 |
  | 12 | Excluir conta → login com as credenciais antigas falha | RN-USER-05 |

- Seletores por **papel e texto acessível** (`getByRole`, `getByLabel`, `getByText`) — nunca por classe CSS ou seletor estrutural.
- `axe-core` executado nas telas principais dentro do E2E ([FE-21](FE-21-acessibilidade-responsividade.md)).
- Rastro de falha: screenshot, vídeo e trace publicados como artefato do CI.

### Não inclui

- E2E de cada validação de formulário — isso é teste de componente, mais rápido e mais preciso.
- Testes de carga ou performance.
- Cobertura de todos os navegadores da matriz em cada execução — ver notas.

## Notas técnicas

- **O fluxo 9 (renovação transparente) é o mais valioso e o mais difícil.** Ele valida a interação entre o token de 15 minutos ([BE-08](../backend/BE-08-emissao-jwt.md)), a rotação com detecção de reuso ([BE-10](../backend/BE-10-refresh-token-rotacao.md)) e o single-flight do cliente ([FE-06](FE-06-interceptor-auth-refresh.md)) — exatamente a combinação que causa desligamento espúrio de sessão em produção. Executar com o backend configurado com `Jwt:AccessTokenMinutes` bem curto (ex.: 5 segundos) em vez de esperar 15 minutos.
- **Nada de `waitForTimeout`.** Esperar por elemento, por resposta de rede ou por estado. Um `sleep` no E2E é um teste flaky esperando para acontecer.
- **Teste flaky é bug** (convenção 4.4): corrigir ou isolar, nunca "re-rodar até passar". Retry no CI mascara o problema — se for usado, o teste entra numa lista de quarentena visível.
- Estratégia de navegadores: **Chromium em todo PR**; a matriz completa (Firefox, WebKit) roda na branch principal ou agendada, para não estourar o tempo do pipeline.
- O fluxo 11 exige criar dois usuários e capturar o id de uma tarefa do segundo — é o teste que prova RN-AUTZ-03 de ponta a ponta.

## Critérios de aceite

- [ ] **CA-01** — Os 12 fluxos da tabela estão implementados e passando.
- [ ] **CA-02** — Um comando único sobe todo o ambiente (frontend, backend, banco) e executa a suíte.
- [ ] **CA-03** — Cada teste cria o próprio usuário e os próprios dados; nenhum depende de dados pré-existentes.
- [ ] **CA-04** — Rodar a suíte em **ordem aleatória** produz o mesmo resultado.
- [ ] **CA-05** — Rodar a suíte **duas vezes seguidas** produz o mesmo resultado, sem limpeza manual entre execuções.
- [ ] **CA-06** — Rodar a suíte **em paralelo** não gera interferência entre testes.
- [ ] **CA-07** — Nenhum teste usa `waitForTimeout` ou espera fixa.
- [ ] **CA-08** — Todos os seletores usam papel, label ou texto acessível — nenhum seletor de classe CSS ou `nth-child`.
- [ ] **CA-09** — O fluxo 9 valida a renovação transparente com token curto configurado, **sem** esperar tempo real de expiração.
- [ ] **CA-10** — No fluxo 9, o usuário conclui a operação **sem ver a tela de login** em nenhum instante.
- [ ] **CA-11** — O fluxo 11 confirma que a tela de "não encontrada" para tarefa de outro usuário é **igual** à de id inexistente (RN-AUTZ-03).
- [ ] **CA-12** — O fluxo 10 valida que o botão "voltar" após o logout **não** exibe a tela autenticada anterior.
- [ ] **CA-13** — Os testes rodam contra o build de **produção** do frontend, não o de desenvolvimento.
- [ ] **CA-14** — A suíte roda no CI a cada PR (Chromium) e completa em tempo aceitável, documentado no PR.
- [ ] **CA-15** — Falhas publicam screenshot, vídeo e trace como artefato, permitindo diagnosticar sem reproduzir localmente.
- [ ] **CA-16** — `axe-core` roda nas telas principais dentro do E2E e falha em violação crítica ou séria.
- [ ] **CA-17** — Ao menos um fluxo é executado em viewport de 360 px.
- [ ] **CA-18** — Não há teste em quarentena ou com retry ao fechar a task; se houver, está documentado com prazo.
- [ ] **CA-19** — Nenhum segredo ou credencial real está no código dos testes.

## Testes obrigatórios

- Esta task **é** o teste. A verificação é a suíte passar de forma estável — CA-04, CA-05 e CA-06 são o que separam uma suíte útil de uma fonte de ruído.
