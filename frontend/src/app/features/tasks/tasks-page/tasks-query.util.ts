import { ParamMap, Params } from '@angular/router';

import {
  DEFAULT_TASKS_FILTERS,
  TaskPriorityFilter,
  TaskStatusFilter,
  TasksFilters,
} from '../../../core/api/models/task.models';

const VALID_STATUSES: readonly TaskStatusFilter[] = ['pending', 'completed', 'all'];
const VALID_PRIORITIES: readonly TaskPriorityFilter[] = ['low', 'medium', 'high'];

/** Página + filtros lidos da URL (FE-16) — a forma que `TasksPageComponent` usa para chamar
 * `TasksStore.load` e para recompor a query string ao navegar. */
export interface TasksQueryState {
  readonly page: number;
  readonly filters: TasksFilters;
}

/** Estado inicial equivalente a uma URL sem nenhum parâmetro (`/tasks`). */
export const DEFAULT_TASKS_QUERY_STATE: TasksQueryState = {
  page: 1,
  filters: DEFAULT_TASKS_FILTERS,
};

/**
 * Lê página e filtros da query string (FE-16). Todo valor ausente ou inválido
 * (`?status=xyz`, `?page=abc`, `?page=-1`, `?page=0`) vira o padrão em silêncio — a tela
 * nunca mostra um erro técnico por causa de um parâmetro estranho na URL (CA-18): o backend
 * responderia 400 se recebesse o valor cru, então o saneamento acontece aqui, antes de
 * qualquer chamada à API.
 */
export function parseTasksQueryParams(paramMap: ParamMap): TasksQueryState {
  return {
    page: parsePage(paramMap.get('page')),
    filters: {
      status: parseStatus(paramMap.get('status')),
      priority: parsePriority(paramMap.getAll('priority')),
      overdue: parseOverdue(paramMap.get('overdue')),
      search: paramMap.get('search')?.trim() ?? '',
    },
  };
}

/**
 * Converte página + filtros de volta para `Params` do `Router` (FE-16, CA-17): cada campo no
 * valor padrão fica **de fora** — `/tasks?status=all&overdue=false` seria ruído. `null` é a
 * forma do `Router` remover um parâmetro que não deve mais aparecer na URL (por exemplo,
 * limpar a busca precisa tirar `search` da URL, não deixar `search=`).
 */
export function buildTasksQueryParams(state: TasksQueryState): Params {
  const { page, filters } = state;
  return {
    page: page > 1 ? page : null,
    status: filters.status !== 'all' ? filters.status : null,
    priority: filters.priority.length > 0 ? [...filters.priority] : null,
    overdue: filters.overdue !== null ? String(filters.overdue) : null,
    search: filters.search.trim() ? filters.search.trim() : null,
  };
}

function parsePage(raw: string | null): number {
  if (raw === null) {
    return 1;
  }
  const value = Number(raw);
  return Number.isInteger(value) && value >= 1 ? value : 1;
}

function parseStatus(raw: string | null): TaskStatusFilter {
  return (VALID_STATUSES as readonly string[]).includes(raw ?? '')
    ? (raw as TaskStatusFilter)
    : 'all';
}

function parsePriority(raw: readonly string[]): readonly TaskPriorityFilter[] {
  const valid = raw.filter((value): value is TaskPriorityFilter =>
    (VALID_PRIORITIES as readonly string[]).includes(value),
  );
  return [...new Set(valid)];
}

function parseOverdue(raw: string | null): boolean | null {
  if (raw === 'true') {
    return true;
  }
  if (raw === 'false') {
    return false;
  }
  return null;
}
