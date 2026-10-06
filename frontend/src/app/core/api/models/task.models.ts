/** União de literais — nenhuma string solta de prioridade em componente (FE-02, CA-02). */
export type TaskPriority = 'Low' | 'Medium' | 'High';

/** União de literais — nenhuma string solta de status em componente (FE-02, CA-02). */
export type TaskStatus = 'Pending' | 'Completed';

/**
 * Corpo de `POST /api/tasks` — espelha `CreateTaskHttpRequest` do Gateway.
 *
 * `priority` e `dueDate` trafegam como `string | null` (não como enum/`Date`) para que um
 * valor inválido caia em erro de validação de campo (400) em vez de erro de binding.
 * `dueDate`, quando presente, é `yyyy-MM-dd` — data pura, sem fuso (FD-17/FD-19): nunca
 * construir com `new Date()` aqui, o deslocamento de fuso desloca o dia.
 */
export interface CreateTaskRequest {
  readonly title: string;
  readonly description: string | null;
  readonly priority: TaskPriority | null;
  readonly dueDate: string | null;
}

/**
 * Corpo de `PUT /api/tasks/{id}` (FE-18) — mesma forma de {@link CreateTaskRequest}, de
 * propósito: são os mesmos quatro campos editáveis. **O `PUT` é substituição total**
 * (BE-19): um campo ausente aqui limpa o valor atual no servidor (descrição/vencimento
 * viram `null`, prioridade volta a `Medium`) — quem monta este objeto sempre preenche os
 * quatro campos com os valores correntes da tela, nunca com um subconjunto.
 */
export type UpdateTaskRequest = CreateTaskRequest;

/** Espelha `TaskHttpResponse` do Gateway. */
export interface TaskResponse {
  readonly id: string;
  readonly title: string;
  readonly description: string | null;
  readonly priority: TaskPriority | null;
  readonly status: TaskStatus;
  /** `yyyy-MM-dd`, sem fuso — não converter para `Date` (FE-02, notas técnicas). */
  readonly dueDate: string | null;
  /** ISO-8601 (UTC); apropriado formatar com `Date` para exibição local. */
  readonly completedAt: string | null;
  /** Calculado pelo backend a partir do `X-Client-Date` enviado (FD-09) — nunca recalcular no cliente. */
  readonly isOverdue: boolean;
  readonly createdAt: string;
  readonly updatedAt: string;
}

/** Resposta de `GET /api/tasks` — espelha o envelope de paginação do Gateway. */
export interface PagedResult<T> {
  readonly items: readonly T[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
}

/** `status` aceito por `GET /api/tasks` (FE-16) — `'all'` equivale a ausente. */
export type TaskStatusFilter = 'pending' | 'completed' | 'all';

/**
 * `priority` aceito por `GET /api/tasks` (FE-16) — minúsculo, como o Gateway espera na
 * query string. Distinto de {@link TaskPriority} (que é o valor no corpo/resposta,
 * capitalizado) de propósito: misturar os dois faria um filtro silenciosamente não
 * combinar com nenhum item.
 */
export type TaskPriorityFilter = 'low' | 'medium' | 'high';

/**
 * Parâmetros de `GET /api/tasks` (FE-16), todos opcionais. Espelha exatamente o contrato do
 * Gateway: `priority` é repetível (disjunção entre valores), os demais se combinam por
 * conjunção. Um filtro no valor "ausente" (`status: 'all'`, `priority: []`, `search: ''`)
 * nunca deve ser enviado à API — quem monta o objeto final antes de chamar {@link TasksApi.list}
 * omite essas chaves.
 */
export interface ListTasksQuery {
  readonly page?: number;
  readonly pageSize?: number;
  readonly status?: TaskStatusFilter;
  readonly priority?: readonly TaskPriorityFilter[];
  readonly overdue?: boolean;
  readonly search?: string;
}

/**
 * Filtros de listagem (FE-16) na forma que a tela mantém — sempre um espelho do que está na
 * URL, nunca uma segunda fonte de verdade (CA-20 de FE-16): quem lê/escreve isto é
 * `TasksPageComponent`, a partir de `ActivatedRoute.queryParamMap`. `overdue: null` significa
 * "sem filtro", distinto de `false`.
 */
export interface TasksFilters {
  readonly status: TaskStatusFilter;
  readonly priority: readonly TaskPriorityFilter[];
  readonly overdue: boolean | null;
  readonly search: string;
}

/** Filtros no estado inicial — nenhum ativo (FE-16). */
export const DEFAULT_TASKS_FILTERS: TasksFilters = {
  status: 'all',
  priority: [],
  overdue: null,
  search: '',
};

/**
 * `true` se ao menos um filtro estiver ativo (FE-16). Usado para distinguir "você ainda não
 * tem tarefas" (`TasksStore.isEmpty`) de "nenhuma tarefa encontrada com esses filtros"
 * (`TasksStore.isFilteredEmpty`).
 */
export function hasActiveTaskFilters(filters: TasksFilters): boolean {
  return (
    filters.status !== 'all' ||
    filters.priority.length > 0 ||
    filters.overdue !== null ||
    filters.search.trim() !== ''
  );
}
