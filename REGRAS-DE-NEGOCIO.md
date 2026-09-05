# Regras de Negócio — Todo List

> Aplicação de lista de tarefas com login básico. Este documento descreve **o quê** o sistema deve fazer (regras de negócio), não **como** implementar.
> Cada regra tem um identificador (`RN-XXX-NN`) para rastreabilidade na quebra em tasks.

Convenções: **DEVE** = obrigatório, **NÃO DEVE** = proibido, **PODE** = opcional. Regras marcadas com ⚠️ dependem de uma decisão em aberto (ver seção 9).

---

## 1. Escopo

O sistema permite que uma pessoa se cadastre, autentique-se e gerencie a sua própria lista de tarefas (criar, editar, concluir, remover e listar). Cada usuário enxerga e manipula **apenas as próprias tarefas**.

**Está no escopo:** cadastro, login, gestão de sessão, CRUD de tarefas, conclusão de tarefas, filtros e ordenação básicos.

**Fora do escopo (nesta versão):** compartilhamento de tarefas entre usuários, colaboração, subtarefas, anexos, notificações/lembretes por e-mail ou push, papéis administrativos, integração com calendário. Ver seção 8.

---

## 2. Atores

| Ator | Descrição |
|---|---|
| **Visitante** | Pessoa não autenticada. Só pode acessar cadastro e login. |
| **Usuário** | Pessoa autenticada. Gerencia as próprias tarefas. |

Nesta versão **não há** papel de administrador.

---

## 3. Conta e autenticação (login básico)

### Cadastro

- **RN-AUTH-01** — Um visitante **PODE** criar uma conta informando **e-mail** e **senha**. ⚠️ *(auto-cadastro liberado — ver D-01)*
- **RN-AUTH-02** — O e-mail **DEVE** ser único no sistema. Não **DEVE** ser possível cadastrar dois usuários com o mesmo e-mail.
- **RN-AUTH-03** — O e-mail **DEVE** ter formato válido.
- **RN-AUTH-04** — A senha **DEVE** ter no mínimo **8 caracteres**, contendo ao menos **uma letra** e **um número**.
- **RN-AUTH-05** — A senha **NÃO DEVE** ser armazenada em texto puro; é sempre guardada de forma irreversível (hash). Nunca é retornada em nenhuma resposta do sistema.
- **RN-AUTH-06** — Ao concluir o cadastro com sucesso, o usuário é considerado **ativo** e apto a autenticar.
- **RN-AUTH-07** — O nome de exibição **PODE** ser informado no cadastro; se ausente, o sistema usa a parte do e-mail antes do `@` como nome padrão.

### Login e sessão

- **RN-AUTH-08** — O login é feito com **e-mail + senha**. Credenciais corretas iniciam uma sessão autenticada.
- **RN-AUTH-09** — Em caso de credenciais inválidas, o sistema **DEVE** retornar uma mensagem genérica ("e-mail ou senha inválidos"), **sem** revelar se o e-mail existe.
- **RN-AUTH-10** — O login bem-sucedido **DEVE** emitir dois tokens: um **token de acesso** (access token), de vida curta, usado para autorizar cada requisição; e um **token de renovação** (refresh token), de vida mais longa, usado para obter novos tokens de acesso.
- **RN-AUTH-11** — O token de acesso **DEVE** expirar rapidamente. Padrão: **15 minutos**. ⚠️ *(duração — ver D-02)*
- **RN-AUTH-12** — O usuário **PODE** encerrar a própria sessão (logout) a qualquer momento. O logout **DEVE** invalidar o refresh token corrente (ver RN-AUTH-19).
- **RN-AUTH-13** — Após **5 tentativas** de login malsucedidas consecutivas para o mesmo e-mail, novas tentativas **DEVEM** ser bloqueadas temporariamente por **15 minutos** (proteção contra força bruta). ⚠️ *(ver D-03)*

### Renovação de token (refresh)

- **RN-AUTH-14** — Enquanto o refresh token for válido, o usuário **PODE** obter um novo token de acesso **sem informar credenciais novamente**, garantindo continuidade da sessão após a expiração do access token.
- **RN-AUTH-15** — O refresh token **DEVE** ter validade maior que a do access token. Padrão: **7 dias**. ⚠️ *(duração — ver D-10)*
- **RN-AUTH-16** — A cada renovação, o sistema **DEVE** emitir um **novo refresh token** e invalidar o anterior (*rotação de refresh token*). Um refresh token só pode ser usado uma única vez. ⚠️ *(rotação — ver D-11)*
- **RN-AUTH-17** — Se um refresh token já utilizado (ou inválido/expirado) for apresentado, a renovação **DEVE** ser negada e a sessão correspondente **DEVE** ser encerrada, exigindo novo login.
- **RN-AUTH-18** — Expirado o refresh token, o usuário **DEVE** se autenticar novamente com e-mail e senha para iniciar nova sessão.
- **RN-AUTH-19** — Refresh tokens **DEVEM** ser revogáveis. No logout (RN-AUTH-12), na troca de senha (RN-AUTH-21) e na desativação/exclusão da conta, os refresh tokens ativos do usuário **DEVEM** ser invalidados.
- **RN-AUTH-20** — Refresh tokens **NÃO DEVEM** ser expostos a código de frontend com acesso amplo nem trafegar/armazenar-se de forma insegura; recebem tratamento mais restrito que o access token. *(Detalhe de armazenamento é decidido na implementação.)*

### Recuperação e alteração

- **RN-AUTH-21** — O usuário autenticado **PODE** alterar a própria senha, informando a senha atual e a nova (a nova segue RN-AUTH-04). A troca de senha **DEVE** invalidar os refresh tokens ativos (RN-AUTH-19).
- **RN-AUTH-22** — Recuperação de senha esquecida ("esqueci minha senha") está **fora do escopo** desta versão. ⚠️ *(ver D-04)*

---

## 4. Usuário

- **RN-USER-01** — Todo usuário possui: identificador único, e-mail, nome de exibição, senha (hash), data de criação e estado (**ativo**/**inativo**).
- **RN-USER-02** — O usuário **PODE** editar o próprio nome de exibição.
- **RN-USER-03** — O e-mail de um usuário **NÃO DEVE** ser alterável nesta versão (é a identidade de login).
- **RN-USER-04** — Um usuário **inativo** **NÃO DEVE** conseguir autenticar-se.
- **RN-USER-05** — Um usuário **PODE** solicitar a exclusão da própria conta. Ao excluir, **todas as suas tarefas DEVEM** ser removidas junto. ⚠️ *(exclusão física vs. anonimização — ver D-05)*

---

## 5. Tarefas

### Estrutura

- **RN-TASK-01** — Uma tarefa possui: identificador único, **título** (obrigatório), **descrição** (opcional), **estado**, **prioridade**, **data de vencimento** (opcional), dono (usuário), data de criação e data de última atualização.
- **RN-TASK-02** — O **título** **DEVE** ter entre **1 e 200 caracteres**, sem espaços em branco como único conteúdo.
- **RN-TASK-03** — A **descrição**, quando informada, **DEVE** ter no máximo **2000 caracteres**.
- **RN-TASK-04** — A **prioridade** **DEVE** ser um destes valores: **Baixa**, **Média** ou **Alta**. O padrão, quando não informada, é **Média**.
- **RN-TASK-05** — A **data de vencimento**, quando informada, **PODE** ser hoje ou uma data futura. O sistema **PODE** aceitar datas passadas, mas **DEVE** sinalizar a tarefa como *atrasada* (ver RN-TASK-16). ⚠️ *(aceitar vencimento no passado — ver D-06)*

### Estados e ciclo de vida

- **RN-TASK-06** — Uma tarefa **DEVE** estar em um destes estados: **Pendente** ou **Concluída**.
- **RN-TASK-07** — Toda tarefa recém-criada nasce no estado **Pendente**.
- **RN-TASK-08** — Uma tarefa **Pendente** **PODE** ser marcada como **Concluída**. Ao concluir, o sistema **DEVE** registrar a **data de conclusão**.
- **RN-TASK-09** — Uma tarefa **Concluída** **PODE** ser reaberta, voltando a **Pendente**; nesse caso a data de conclusão **DEVE** ser limpa.

### Operações (CRUD)

- **RN-TASK-10** — O usuário **PODE** criar uma nova tarefa informando ao menos o título; os demais campos seguem os padrões definidos acima.
- **RN-TASK-11** — O usuário **PODE** editar título, descrição, prioridade e data de vencimento das próprias tarefas.
- **RN-TASK-12** — O usuário **PODE** remover as próprias tarefas.
- **RN-TASK-13** — A remoção é **lógica** (soft delete): a tarefa deixa de aparecer nas listagens, mas é mantida internamente por período definido antes da remoção definitiva. ⚠️ *(soft vs. hard delete — ver D-07)*
- **RN-TASK-14** — Toda alteração em uma tarefa **DEVE** atualizar a sua data de última atualização.

### Limites e derivações

- **RN-TASK-15** — Um usuário **PODE** ter no máximo **500 tarefas ativas** (não concluídas e não removidas). Ao atingir o limite, a criação de novas tarefas **DEVE** ser bloqueada com mensagem clara. ⚠️ *(existência e valor do limite — ver D-08)*
- **RN-TASK-16** — Uma tarefa é considerada **atrasada** quando está **Pendente** e sua data de vencimento é anterior à data atual. "Atrasada" é uma condição derivada, **não** um estado próprio.

---

## 6. Propriedade e autorização

- **RN-AUTZ-01** — Toda tarefa pertence a exatamente **um** usuário (o criador).
- **RN-AUTZ-02** — Um usuário **DEVE** conseguir ver, editar, concluir e remover **apenas as próprias tarefas**.
- **RN-AUTZ-03** — Qualquer tentativa de acessar ou manipular uma tarefa de outro usuário **DEVE** ser negada, retornando o mesmo resultado de "não encontrada" (sem revelar a existência do recurso).
- **RN-AUTZ-04** — Todas as operações sobre tarefas **DEVEM** exigir sessão autenticada. Visitantes **NÃO DEVEM** acessar nenhuma tarefa.

---

## 7. Listagem, filtros e ordenação

- **RN-LIST-01** — A listagem de tarefas **DEVE** retornar somente as tarefas do usuário autenticado e não removidas.
- **RN-LIST-02** — O usuário **PODE** filtrar as tarefas por **estado** (pendentes, concluídas, todas).
- **RN-LIST-03** — O usuário **PODE** filtrar por **prioridade**.
- **RN-LIST-04** — O usuário **PODE** filtrar por **atrasadas** (conforme RN-TASK-16).
- **RN-LIST-05** — O usuário **PODE** buscar tarefas por texto no **título** (e opcionalmente na descrição).
- **RN-LIST-06** — A ordenação padrão é: **pendentes antes de concluídas**, depois por **data de vencimento crescente** (tarefas sem vencimento por último) e, por fim, por **data de criação**.
- **RN-LIST-07** — A listagem **DEVE** ser paginada, com um tamanho de página padrão. ⚠️ *(tamanho — ver D-09)*

---

## 8. Fora do escopo (não-objetivos desta versão)

Registrados para evitar ambiguidade — **não** devem ser implementados agora:

- Compartilhamento ou atribuição de tarefas a outros usuários.
- Subtarefas, checklists aninhados ou dependências entre tarefas.
- Anexos, comentários ou histórico de alterações visível.
- Notificações, lembretes ou e-mails.
- Tarefas recorrentes.
- Múltiplas listas/projetos/categorias por usuário (a lista é única e plana).
- Papéis administrativos e gestão de outros usuários.
- Login social (Google, etc.) e autenticação em duas etapas.

---

## 9. Decisões a confirmar

Padrões já adotados no documento; ajustar conforme a definição de produto. Cada decisão altera as RNs indicadas.

| # | Decisão | Padrão adotado | Afeta |
|---|---|---|---|
| **D-01** | Auto-cadastro é liberado a qualquer visitante? | Sim, liberado | RN-AUTH-01 |
| **D-02** | Duração do token de acesso | 15 minutos | RN-AUTH-11 |
| **D-03** | Bloqueio por tentativas de login | 5 tentativas → 15 min | RN-AUTH-13 |
| **D-04** | "Esqueci minha senha" nesta versão? | Fora do escopo | RN-AUTH-22 |
| **D-05** | Exclusão de conta: apagar tudo ou anonimizar? | Apagar conta e tarefas | RN-USER-05 |
| **D-06** | Aceitar vencimento no passado? | Aceita, marca como atrasada | RN-TASK-05 |
| **D-07** | Remoção de tarefa: lógica ou física? | Lógica (soft delete) | RN-TASK-13 |
| **D-08** | Existe limite de tarefas por usuário? | Sim, 500 ativas | RN-TASK-15 |
| **D-09** | Tamanho padrão da página na listagem | A definir (ex.: 20) | RN-LIST-07 |
| **D-10** | Duração do refresh token | 7 dias | RN-AUTH-15 |
| **D-11** | Rotação de refresh token (uso único)? | Sim, com rotação | RN-AUTH-16 |

---

## 10. Glossário

- **Tarefa (todo):** item da lista que o usuário quer acompanhar.
- **Pendente:** tarefa ainda não concluída.
- **Concluída:** tarefa marcada como feita, com data de conclusão registrada.
- **Atrasada:** tarefa pendente cuja data de vencimento já passou (condição derivada).
- **Soft delete:** remoção lógica — some das listagens, mas permanece internamente por um período.
- **Sessão:** período em que o usuário está autenticado, delimitado por login e expiração/logout.
- **Token de acesso (access token):** credencial de vida curta que autoriza cada requisição do usuário.
- **Token de renovação (refresh token):** credencial de vida mais longa usada para obter novos tokens de acesso sem novo login.
- **Rotação de refresh token:** prática de emitir um novo refresh token a cada renovação e invalidar o anterior (uso único).

---

*Próximo passo sugerido: revisar as decisões da seção 9 e, em seguida, quebrar as RNs em tasks de implementação (por domínio: Autenticação, Usuário, Tarefas, Listagem).*
