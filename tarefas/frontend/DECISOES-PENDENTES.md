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

> **Nota (21/09/2026, recorte do T2):** o desenho acima — refresh token em cookie `HttpOnly`, bootstrap de sessão por refresh — é o desenho **de longo prazo** e continua valendo como alvo. Ele depende de [BE-10](../backend/BE-10-refresh-token-rotacao.md), que **não existe no backend do T2** (D-36: o login do T2 emite só um access token, sem refresh, sem cookie). Enquanto isso, o frontend do T2 guarda o access token **só em memória**, sem storage nenhum — não porque FD-01 mudou, mas porque a metade que ela protegia (o refresh token) ainda não existe para proteger. Ver a nova **FD-20** para o desenho específico do T2. Quando [BE-10](../backend/BE-10-refresh-token-rotacao.md) existir, vale o desenho original desta seção, sem alteração.

### FD-16 ✅ — Front e API na mesma origem — e essa origem é o **API Gateway**

`SameSite=Strict` é viável, **não há CSRF token** a implementar e **não há CORS a configurar**. Em contrapartida, toda chamada a `/api/auth/*` precisa de `withCredentials: true` — sem isso o cookie não é anexado, e o sintoma é um 401 no refresh que parece bug de sessão. Ver **D-21** no backend.

**Emenda (backend dividido em dois serviços).** O backend passou a ser dois serviços — Identity e Tasks —, em portas distintas. Isso **não** transforma o frontend em cliente de duas origens: o **API Gateway** é a origem única, e o frontend fala **só** com ele.

```
navegador ──HTTPS/JSON──▶ API Gateway ──┬──gRPC──▶ Identity Service
   (origem única)                        └──gRPC──▶ Tasks Service
```

`/api/auth/*` e `/api/tasks` continuam sendo rotas da mesma origem; o cookie com `Path=/api/auth` (**FD-01**) continua correto; `SameSite=Strict` continua viável. **Nenhuma task FE muda por causa disso** — a divisão do backend é invisível daqui.

**O que NÃO fazer no meio do caminho:** enquanto o Gateway não existir, pode ser tentador apontar o frontend direto para as duas portas e "resolver com CORS". Isso seria trabalho descartado, quebraria `SameSite=Strict` (exigindo `SameSite=None` e proteção CSRF de volta) e mascararia o desenho correto. Ver **D-32** no backend.

> **Emenda (21/09/2026, recorte do T2):** na VM e no container, a origem única passa a ser o **nginx**, não mais o API Gateway sozinho — o nginx serve o build estático do Angular e faz proxy reverso de `/api` para o Gateway (decisão de backend **D-40**, task [BE-42](../backend/BE-42-nginx-mesma-origem.md)). O Gateway continua sendo o **único ponto de entrada da API** — o nginx não fala gRPC nem toma decisão de negócio, só serve arquivos e repassa `/api`. Continua **sem CORS a configurar** e **sem CSRF token**, porque front e API seguem na mesma origem do ponto de vista do navegador — só que essa origem agora é o nginx, não mais o Gateway diretamente. Em desenvolvimento, o `proxy.conf.json` do `ng serve` ([FE-01](FE-01-fundacao-workspace.md)) reproduz a mesma origem, apontando `/api` para `http://localhost:8080`. **Continua proibido** apontar o frontend direto para o Gateway com CORS habilitado — o raciocínio de "o que NÃO fazer" acima vale integralmente, trocando "Gateway" por "nginx" onde se lê "origem única".

> ```
> navegador ──HTTPS/JSON──▶ nginx ──┬── serve o build do Angular (estático)
>    (origem única)                 └──proxy /api──▶ API Gateway ──gRPC──▶ Identity / Tasks
> ```

### FD-17 ✅ — "Atrasada" usa a data local do usuário

O frontend envia o header **`X-Client-Date: yyyy-MM-dd`** em toda requisição à API, por interceptor ([FE-02](FE-02-contratos-camada-http.md)). O backend o usa para calcular `isOverdue` e o filtro `overdue` (**D-18**).

**Por que header e não query param:** `isOverdue` aparece na resposta de cinco endpoints. Um interceptor resolve os cinco de uma vez; um parâmetro exigiria repetição em cada chamada, e bastaria esquecer um para a tela ficar inconsistente.

Isso **não** altera FD-09: o frontend continua **lendo** `isOverdue` da resposta, sem recalcular. Ele informa a data; quem decide é o backend.

### FD-18 ✅ — API sem versionamento

Rotas permanecem `/api/...`. Front e back são implantados juntos (ver **D-22** no backend). A consequência aceita é que toda mudança incompatível de contrato exige implantação coordenada.

### FD-20 ✅ — Sessão do T2 só em memória (21/09/2026)

> Nota de numeração: o pedido original chamava esta decisão de "FD-19", mas esse número já estava em uso na tabela de "Demais decisões" (a questão do `Intl.DateTimeFormat` para `X-Client-Date`). Esta entrada ocupa **FD-20**, o próximo número livre.

**O conflito que motivou:** FD-01 desenha refresh token em cookie `HttpOnly` com bootstrap de sessão por refresh. Esse desenho depende de [BE-10](../backend/BE-10-refresh-token-rotacao.md) (refresh token e rotação), que **não existe no backend do T2** — o login do T2 (D-36) emite só um access token, sem refresh, sem cookie, sem endpoint de logout no servidor.

**Decisão:** no T2, o `SessionStore` ([FE-05](FE-05-estado-sessao.md)) guarda o access token **só em memória** (signal), exatamente como FD-01 já previa para o access token — a diferença é que não há refresh token nenhum para complementar. Sem cookie, sem `localStorage`, sem `sessionStorage`.

**Motivo:** é a única opção consistente com o que o backend do T2 oferece. Guardar o access token em `localStorage` "para sobreviver ao F5" contradiria RN-AUTH-05/RN-AUTH-20 e a própria FD-01, trocando um risco de XSS pequeno (15 minutos em memória) por um grande (token de vida longa em storage acessível a JavaScript) só por conveniência de não precisar logar de novo.

**Consequência:** recarregar a página (`F5`) numa rota autenticada **exige novo login** — não há bootstrap de sessão no T2, porque não há refresh a partir do qual restaurá-la. [FE-05](FE-05-estado-sessao.md) e [FE-06](FE-06-interceptor-auth-refresh.md) entram no T2 sem o bloco de bootstrap/renovação (ver os blocos "Recorte do T2" de cada task).

**Condição de revisão:** quando [BE-10](../backend/BE-10-refresh-token-rotacao.md) existir, esta decisão é superada pelo desenho original de **FD-01** — cookie `HttpOnly`, bootstrap por refresh, sem mudança de rumo, só a continuação do que já estava planejado.

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
