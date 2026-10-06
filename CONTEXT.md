# TodoList

Lista de tarefas pessoal: cada pessoa se cadastra, autentica-se e gerencia apenas as próprias tarefas.
Não há compartilhamento, colaboração nem papel de administrador.

## Linguagem

O termo canônico é o português; o nome entre parênteses é como o conceito aparece no código.

### Usuários e acesso

**Visitante**:
Pessoa não autenticada. Só pode se cadastrar e fazer login.
_Evitar_: anônimo, convidado

**Usuário** (`User`):
Pessoa cadastrada, identificada pelo e-mail, que gerencia a própria lista de tarefas. Não tem estado: existe ou foi excluído.
_Evitar_: conta (exceto na expressão "excluir a conta"), cliente, usuário ativo, usuário inativo

**Nome de exibição** (`DisplayName`):
Nome pelo qual o usuário é tratado na interface. É o único dado do usuário que ele pode editar.
_Evitar_: apelido, username

**Sessão** (`SessionId`):
Um login em um dispositivo, do login até o logout, a expiração ou a revogação. Um usuário pode ter várias sessões ao mesmo tempo.
_Evitar_: período autenticado, login (como substantivo para a sessão)

**Token de acesso** (access token):
Credencial de vida curta que autoriza cada requisição de uma sessão.

**Token de renovação** (`RefreshToken`):
Credencial de vida longa e uso único que mantém a sessão viva, trocada por um novo par de tokens a cada renovação.

**Rotação**:
Troca do token de renovação por um novo a cada uso, dentro da mesma sessão.

**Revogação**:
Encerramento forçado de uma ou de todas as sessões de um usuário antes do vencimento.
_Evitar_: invalidação, cancelamento

**Bloqueio de login** (`Lockout`):
Recusa temporária de tentativas de login para um e-mail, exista ou não um usuário com ele.
_Evitar_: conta bloqueada, usuário bloqueado

**Excluir**:
Apagar um usuário de forma definitiva e imediata, junto com todas as suas tarefas e sessões.
_Evitar_: desativar, remover (reservado para tarefa)

### Tarefas

**Tarefa** (`TodoTask`):
Item que o usuário quer acompanhar, com título, prioridade e, opcionalmente, descrição e vencimento.
_Evitar_: todo, item, afazer

**Dono** (`OwnerId`):
O usuário a quem a tarefa pertence. Toda tarefa tem exatamente um dono, que nunca muda.
_Evitar_: autor, criador, responsável

**Lista de tarefas**:
O conjunto das tarefas não removidas de um usuário. Cada usuário tem uma só, sem pastas, projetos ou categorias.

**Pendente** (`Pending`):
Tarefa ainda não concluída. É o estado em que toda tarefa nasce.
_Evitar_: ativa, aberta, em andamento

**Concluída** (`Completed`):
Tarefa marcada como feita, com a data de conclusão registrada. Pode ser reaberta, voltando a pendente.
_Evitar_: finalizada, fechada, feita

**Vencimento** (`DueDate`):
Data, sem hora, até a qual o usuário pretende concluir a tarefa. Pode estar no passado.
_Evitar_: prazo, deadline

**Atrasada** (`IsOverdue`):
Tarefa pendente cujo vencimento é anterior ao dia de hoje do usuário. É uma condição derivada, não um estado.
_Evitar_: vencida, expirada

**Prioridade** (`TaskPriority`):
Importância relativa da tarefa: Baixa, Média ou Alta.

**Limite de tarefas**:
Número máximo de tarefas pendentes que um usuário pode ter ao mesmo tempo.
_Evitar_: limite de tarefas ativas

**Remover** (`SoftDelete`):
Tirar uma tarefa da lista do usuário. A tarefa deixa de aparecer, mas permanece guardada durante a retenção.
_Evitar_: deletar, excluir (reservado para usuário), apagar

**Retenção**:
Período em que uma tarefa removida permanece guardada antes de ser expurgada.

**Expurgar** (`Purge`):
Apagar de forma definitiva e automática o que já passou da retenção.
_Evitar_: remoção definitiva, limpeza
