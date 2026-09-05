# Decisões pendentes — frontend

Decisões que a quebra do frontend exigiu e que não estão respondidas em [REGRAS-DE-NEGOCIO.md](../../REGRAS-DE-NEGOCIO.md) nem em [CONVENCOES-CODIGO.md](../../CONVENCOES-CODIGO.md). Todas têm padrão adotado; nenhuma task está bloqueada.

---

## Decisões fechadas (20/08/2026)

### FD-01 ✅ — Refresh token em cookie `HttpOnly`, nunca em storage acessível a JavaScript

**O conflito que motivou:** a RN-AUTH-20 exige que o refresh token não seja exposto a código de frontend com acesso amplo. O contrato original de [BE-09](../backend/BE-09-login.md)/[BE-10](../backend/BE-10-refresh-token-rotacao.md) o entregava no corpo JSON, o que obrigaria o frontend a guardá-lo onde JavaScript alcança — exatamente o proibido. Um XSS levaria embora uma credencial de 7 dias.

**Decisão:** o backend emite o refresh token como cookie `HttpOnly; Secure; SameSite=Strict; Path=/api/auth`. O navegador o envia sozinho; o frontend **nunca lê o valor**. O access token continua no corpo, porque precisa ir no header `Authorization` — e sua vida de 15 minutos é a mitigação.

**Ponto que costuma ser mal entendido:** o ganho **não** é o envio automático. Automação você já teria com um interceptor lendo o `localStorage`, e ela não protegeria nada. O envio automático é apenas o *mecanismo* que permite o valor ser ilegível ao próprio frontend — que é o ganho real.

**Limite honesto:** um XSS ainda consegue *chamar* `/api/auth/refresh` na própria origem e obter um access token. O que ele não consegue é exfiltrar a credencial de longa duração para usar depois, de outro lugar.

**Consequência:** emendou BE-09, BE-10 e BE-11 (ver **D-20** em [backend/DECISOES-PENDENTES.md](../backend/DECISOES-PENDENTES.md)) e **simplificou** [FE-05](FE-05-estado-sessao.md) — a abstração `TokenStorage` com duas implementações deixou de ser necessária, porque o frontend não armazena mais nada.

### FD-16 ✅ — Front e API na mesma origem — e essa origem é o **API Gateway**

`SameSite=Strict` é viável, **não há CSRF token** a implementar e **não há CORS a configurar**. Em contrapartida, toda chamada a `/api/auth/*` precisa de `withCredentials: true` — sem isso o cookie não é anexado, e o sintoma é um 401 no refresh que parece bug de sessão. Ver **D-21** no backend.

**Emenda (backend dividido em dois serviços).** O backend passou a ser dois serviços — Identity e Tasks —, em portas distintas. Isso **não** transforma o frontend em cliente de duas origens: o **API Gateway** é a origem única, e o frontend fala **só** com ele.

```
navegador ──HTTPS/JSON──▶ API Gateway ──┬──gRPC──▶ Identity Service
   (origem única)                        └──gRPC──▶ Tasks Service
```

`/api/auth/*` e `/api/tasks` continuam sendo rotas da mesma origem; o cookie com `Path=/api/auth` (**FD-01**) continua correto; `SameSite=Strict` continua viável. **Nenhuma task FE muda por causa disso** — a divisão do backend é invisível daqui.

**O que NÃO fazer no meio do caminho:** enquanto o Gateway não existir, pode ser tentador apontar o frontend direto para as duas portas e "resolver com CORS". Isso seria trabalho descartado, quebraria `SameSite=Strict` (exigindo `SameSite=None` e proteção CSRF de volta) e mascararia o desenho correto. Ver **D-32** no backend.

### FD-17 ✅ — "Atrasada" usa a data local do usuário

O frontend envia o header **`X-Client-Date: yyyy-MM-dd`** em toda requisição à API, por interceptor ([FE-02](FE-02-contratos-camada-http.md)). O backend o usa para calcular `isOverdue` e o filtro `overdue` (**D-18**).

**Por que header e não query param:** `isOverdue` aparece na resposta de cinco endpoints. Um interceptor resolve os cinco de uma vez; um parâmetro exigiria repetição em cada chamada, e bastaria esquecer um para a tela ficar inconsistente.

Isso **não** altera FD-09: o frontend continua **lendo** `isOverdue` da resposta, sem recalcular. Ele informa a data; quem decide é o backend.

### FD-18 ✅ — API sem versionamento

Rotas permanecem `/api/...`. Front e back são implantados juntos (ver **D-22** no backend). A consequência aceita é que toda mudança incompatível de contrato exige implantação coordenada.

---

## Demais decisões

| # | Questão | Padrão provisório | Task |
|---|---|---|---|
| **FD-02** | Biblioteca de UI | **Angular Material 22** — acessibilidade pronta e componentes de formulário/diálogo maduros, que é onde o app gasta a maior parte do esforço | [FE-04](FE-04-layout-design-base.md) |
| **FD-03** | Idioma da interface | **pt-BR**, com todos os textos centralizados em constantes. i18n multi-idioma fora do escopo | [FE-03](FE-03-erros-feedback.md), [FE-04](FE-04-layout-design-base.md) |
| **FD-04** | SSR / prerender | **Não** — a aplicação inteira fica atrás de login; SSR não traz SEO nem ganho perceptível aqui | [FE-01](FE-01-fundacao-workspace.md) |
| **FD-05** | Gerenciamento de estado | **Serviços com signals**. NgRx SignalStore não entra "por precaução" (convenção 2.2) | [FE-05](FE-05-estado-sessao.md), [FE-14](FE-14-servico-estado-tarefas.md) |
| **FD-19** | Como obter a data local para o header `X-Client-Date`? | `Intl.DateTimeFormat` / data local do navegador, formatada como `yyyy-MM-dd` **sem** passar por conversão UTC | [FE-02](FE-02-contratos-camada-http.md) |
| **FD-06** | Atualização otimista ao concluir/reabrir | **Sim**, com rollback em caso de erro — a ação é frequente e a latência é perceptível | [FE-19](FE-19-concluir-reabrir.md) |
| **FD-07** | Criar/editar tarefa: rota dedicada ou diálogo? | **Rota dedicada** (`/tasks/new`, `/tasks/:id/edit`) — deep link, botão voltar e foco funcionam sem esforço extra | [FE-17](FE-17-criar-tarefa.md), [FE-18](FE-18-editar-tarefa.md) |
| **FD-08** | Filtros e paginação na URL | **Sim**, como query params — a lista filtrada é compartilhável e sobrevive ao reload | [FE-16](FE-16-filtros-busca-url.md) |
| **FD-09** | "Atrasada": recalcular no cliente ou usar `isOverdue` da API? | **Usar `isOverdue` da API** — o cliente informa a data local (FD-17), mas quem calcula é o backend; dois cálculos independentes divergiriam | [FE-15](FE-15-listagem-paginacao.md) |
| **FD-10** | Paginação clássica ou rolagem infinita? | **Paginação clássica** — casa com o contrato `page`/`pageSize` do backend e é mais acessível | [FE-15](FE-15-listagem-paginacao.md) |
| **FD-11** | Formulários | **Signal Forms** (estáveis no v22, exigidos pela convenção 2.2 para telas novas) | [FE-08](FE-08-tela-cadastro.md) em diante |
| **FD-12** | Tema escuro | Fora do escopo desta versão; tokens de cor preparados para permitir depois | [FE-04](FE-04-layout-design-base.md) |
| **FD-13** | Renovação de token: proativa (antes de expirar) ou reativa (após 401)? | **Ambas** — proativa evita o 401 no caminho feliz; reativa cobre o relógio dessincronizado | [FE-06](FE-06-interceptor-auth-refresh.md) |
| **FD-14** | Validação de senha no cliente espelha a política do backend? | **Sim**, para feedback imediato — mas o backend continua sendo a autoridade | [FE-08](FE-08-tela-cadastro.md), [FE-12](FE-12-alteracao-senha.md) |
| **FD-15** | Confirmação antes de remover tarefa | **Sim**, diálogo de confirmação — a remoção não tem "desfazer" nesta versão | [FE-20](FE-20-remover-tarefa.md) |
