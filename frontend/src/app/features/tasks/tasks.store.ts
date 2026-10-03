import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { Observable, catchError, tap, throwError } from 'rxjs';

import { TasksApi } from '../../core/api/tasks-api.service';
import {
  CreateTaskRequest,
  DEFAULT_TASKS_FILTERS,
  ListTasksQuery,
  TaskResponse,
  TasksFilters,
  UpdateTaskRequest,
  hasActiveTaskFilters,
} from '../../core/api/models/task.models';
import { SessionStore } from '../../core/auth/session-store';
import { AppError } from '../../core/errors/app-error.model';

const DEFAULT_PAGE_SIZE = 20;

/** Estado assíncrono de carregamento da listagem (FE-14). */
export type TasksStatus = 'idle' | 'loading' | 'success' | 'error';

/**
 * Camada única de estado de tarefas (FE-14), exposta por signals: `load` (paginado e
 * filtrado — FE-16), `create`, `getById`, `update`, `complete`, `reopen` e `remove`. Nenhum
 * componente injeta `TasksApi` diretamente; sempre este store.
 *
 * **Filtros são um espelho, não uma segunda fonte de verdade (FE-16, CA-20):** este store
 * não decide filtro nenhum sozinho — `filters` é só o que `load()` recebeu da última vez,
 * para os `computed` de estado vazio (`isEmpty`/`isFilteredEmpty`) saberem se a lista vazia
 * é "sem tarefas" ou "sem resultado para o filtro". Quem lê a URL e decide os filtros é
 * `TasksPageComponent`.
 *
 * **Como cada mutação reflete na lista (atualizado em 24/09/2026, FE-16):**
 *
 * - `update` continua só trocando o item na lista pela resposta do servidor: os quatro
 *   campos editáveis por `PUT` não incluem o `status`, então editar nunca move o item entre
 *   o bloco de pendentes e o de concluídas de RN-LIST-06 — recarregar a página não traria
 *   nenhuma correção de ordenação que valha o custo.
 * - `complete`/`reopen` **mudaram**: a dívida da Fase 1 (comentário antigo abaixo, mantido
 *   como histórico) valia enquanto a ordenação era só por `createdAt` decrescente. Com
 *   RN-LIST-06 (Fase 2, BE-22), pendentes vêm antes de concluídas — concluir ou reabrir uma
 *   tarefa **muda sua posição** na lista, às vezes para outra página. Continuar só trocando
 *   o item em memória deixaria a tarefa na posição errada até o próximo `load()` manual.
 *   A correção: depois que o servidor confirma a transição, o store recarrega a página
 *   atual em silêncio — sem passar `status` por `'loading'` — para não substituir a lista
 *   inteira por um spinner (o "piscar a cada ação" que a Fase 1 já tinha evitado); os
 *   demais itens só trocam de referência se a página realmente mudar de conteúdo. Se esse
 *   recarregamento falhar, a ação em si (já confirmada pelo servidor) não é desfeita — só a
 *   correção de ordenação fica pendente para o próximo `load()`.
 *
 * Histórico (decisão de 23/09/2026, terceira onda da Fase 1, superada acima): "nenhuma
 * mutação recarrega a página inteira — só troca o item afetado no array em memória, ou o
 * remove. Válido enquanto a ordenação não dependia do estado da tarefa."
 *
 * `remove` continua diferente: afeta `totalCount` e pode esvaziar a página atual, então quem
 * decide se recua uma página é o container (`TasksPageComponent`), não este store.
 */
@Injectable({ providedIn: 'root' })
export class TasksStore {
  private readonly tasksApi = inject(TasksApi);
  private readonly session = inject(SessionStore);

  private readonly itemsSignal = signal<readonly TaskResponse[]>([]);
  private readonly pageSignal = signal(1);
  private readonly pageSizeSignal = signal(DEFAULT_PAGE_SIZE);
  private readonly totalCountSignal = signal(0);
  private readonly statusSignal = signal<TasksStatus>('idle');
  private readonly errorSignal = signal<AppError | null>(null);
  private readonly filtersSignal = signal<TasksFilters>(DEFAULT_TASKS_FILTERS);

  /** Contador de requisições em voo — protege contra respostas fora de ordem (`load`,
   * inclusive o recarregamento silencioso pós-`complete`/`reopen`, e a busca com debounce
   * de FE-16, CA-11). */
  private latestRequestId = 0;

  readonly items = this.itemsSignal.asReadonly();
  readonly page = this.pageSignal.asReadonly();
  readonly pageSize = this.pageSizeSignal.asReadonly();
  readonly totalCount = this.totalCountSignal.asReadonly();
  readonly status = this.statusSignal.asReadonly();
  readonly error = this.errorSignal.asReadonly();
  readonly filters = this.filtersSignal.asReadonly();

  /** Calculado no cliente a partir de `totalCount` — o backend não devolve `totalPages`. */
  readonly totalPages = computed(() =>
    Math.max(1, Math.ceil(this.totalCountSignal() / this.pageSizeSignal())),
  );

  /** `true` se a última chamada a `load()` trazia algum filtro ativo (FE-16). */
  readonly hasActiveFilters = computed(() => hasActiveTaskFilters(this.filtersSignal()));

  /** `true` só em sucesso com zero itens **e sem filtro ativo** — "você ainda não tem
   * tarefas" (FE-16 distingue isto de {@link isFilteredEmpty}). Nunca durante carregamento
   * ou erro. */
  readonly isEmpty = computed(
    () =>
      this.statusSignal() === 'success' &&
      this.itemsSignal().length === 0 &&
      !this.hasActiveFilters(),
  );

  /** `true` só em sucesso com zero itens **com algum filtro ativo** — "nenhuma tarefa
   * encontrada com esses filtros" (FE-16, CA-13). */
  readonly isFilteredEmpty = computed(
    () =>
      this.statusSignal() === 'success' &&
      this.itemsSignal().length === 0 &&
      this.hasActiveFilters(),
  );

  constructor() {
    // Ao encerrar a sessão, nenhum dado de tarefa deve sobreviver para o próximo usuário
    // que logar na mesma aba (README de features/tasks/, CA-07 de FE-10, CA-14 de FE-05).
    // `SessionStore` não conhece este store — é este quem observa `isAuthenticated`.
    effect(() => {
      if (!this.session.isAuthenticated()) {
        this.clear();
      }
    });
  }

  /** Carrega uma página de tarefas com os filtros informados (FE-16); `filters` ausente
   * repete o último usado (útil para paginar/repetir sem alterar filtro). */
  load(
    page = 1,
    pageSize = this.pageSizeSignal(),
    filters: TasksFilters = this.filtersSignal(),
  ): void {
    this.filtersSignal.set(filters);
    this.fetchPage(page, pageSize, filters, { showLoading: true });
  }

  /**
   * Cria uma tarefa. Quem chama decide o que fazer após o sucesso (voltar à lista e
   * recarregar) — este store não navega nem recarrega sozinho.
   */
  create(request: CreateTaskRequest): Observable<TaskResponse> {
    return this.tasksApi.create(request);
  }

  /** Obtém uma tarefa por id — usado por telas que precisam de uma tarefa isolada. */
  getById(id: string): Observable<TaskResponse> {
    return this.tasksApi.getById(id);
  }

  /**
   * Substitui uma tarefa (FE-18, `PUT`, semântica de substituição total — BE-19). Quem
   * monta `request` (via `TaskFormComponent`) já garante os quatro campos com os valores
   * correntes da tela — a "armadilha" do `PUT` parcial é resolvida lá, não aqui; este
   * método só chama a API e, em sucesso, troca o item na lista pela resposta do servidor
   * (ver o comentário de classe sobre por que não recarrega a página).
   */
  update(id: string, request: UpdateTaskRequest): Observable<TaskResponse> {
    return this.tasksApi.update(id, request).pipe(tap((task) => this.replaceItem(task)));
  }

  /**
   * Conclui uma tarefa pendente (FE-19, BE-20) com atualização otimista e rollback (FD-06):
   * o item muda para "Completed" antes da resposta do servidor; se a chamada falhar, volta
   * ao estado anterior. Em sucesso, o item é substituído pelo que o servidor devolveu e a
   * página atual é recarregada em silêncio, para refletir a nova posição do item
   * (RN-LIST-06 — ver o comentário de classe). Em 404, o item sai da lista (RN-AUTZ-03), sem
   * recarregar (não há nova posição a refletir para um item que não existe mais).
   */
  complete(id: string): Observable<TaskResponse> {
    return this.transition(
      id,
      (task) => ({
        ...task,
        status: 'Completed',
        completedAt: new Date().toISOString(),
        isOverdue: false,
      }),
      () => this.tasksApi.complete(id),
    );
  }

  /** Reabre uma tarefa concluída (FE-19, BE-20) — mesmo desenho de {@link complete}. */
  reopen(id: string): Observable<TaskResponse> {
    return this.transition(
      id,
      (task) => ({ ...task, status: 'Pending', completedAt: null }),
      () => this.tasksApi.reopen(id),
    );
  }

  /**
   * Remove uma tarefa (FE-20, soft delete no servidor — BE-21). **Sem otimismo**: o item só
   * sai da lista depois do 204 (FE-20, notas técnicas) — diferente de `complete`/`reopen`,
   * remover é o tipo de erro que a interface não pode fingir que não aconteceu. Em 404
   * (RN-AUTZ-03), o item sai da lista mesmo assim: se o servidor diz que não existe (mais)
   * para este usuário, mantê-lo na tela seria mentir por omissão.
   */
  remove(id: string): Observable<void> {
    return this.tasksApi.remove(id).pipe(
      tap(() => this.dropItem(id)),
      catchError((error: AppError) => {
        if (error.status === 404) {
          this.dropItem(id);
        }
        return throwError(() => error);
      }),
    );
  }

  /** Esvazia o estado — chamado internamente ao encerrar a sessão. */
  clear(): void {
    this.itemsSignal.set([]);
    this.pageSignal.set(1);
    this.pageSizeSignal.set(DEFAULT_PAGE_SIZE);
    this.totalCountSignal.set(0);
    this.statusSignal.set('idle');
    this.errorSignal.set(null);
    this.filtersSignal.set(DEFAULT_TASKS_FILTERS);
  }

  private replaceItem(task: TaskResponse): void {
    this.itemsSignal.set(this.itemsSignal().map((item) => (item.id === task.id ? task : item)));
  }

  private dropItem(id: string): void {
    if (!this.itemsSignal().some((item) => item.id === id)) {
      return;
    }
    this.itemsSignal.set(this.itemsSignal().filter((item) => item.id !== id));
    this.totalCountSignal.update((count) => Math.max(0, count - 1));
  }

  /**
   * Busca uma página no servidor. `showLoading` distingue os dois chamadores: `load()`
   * (`true`, alterna `status` para `'loading'`/`'error'`, o jeito normal de FE-15) e o
   * recarregamento silencioso pós-`complete`/`reopen` (`false`, nunca mexe em `status` — a
   * lista permanece visível, e um erro aqui não vira tela de erro, já que a ação que o
   * disparou já teve sucesso).
   */
  private fetchPage(
    page: number,
    pageSize: number,
    filters: TasksFilters,
    options: { showLoading: boolean },
  ): void {
    if (options.showLoading) {
      this.statusSignal.set('loading');
      this.errorSignal.set(null);
    }
    const requestId = ++this.latestRequestId;
    const query: ListTasksQuery = {
      page,
      pageSize,
      status: filters.status,
      priority: filters.priority,
      overdue: filters.overdue ?? undefined,
      search: filters.search,
    };

    this.tasksApi.list(query).subscribe({
      next: (result) => {
        if (requestId !== this.latestRequestId) {
          return; // resposta de uma chamada anterior, já superada por uma mais recente
        }
        this.itemsSignal.set(result.items);
        this.pageSignal.set(result.page);
        this.pageSizeSignal.set(result.pageSize);
        this.totalCountSignal.set(result.totalCount);
        this.statusSignal.set('success');
      },
      error: (error: AppError) => {
        if (requestId !== this.latestRequestId) {
          return;
        }
        if (options.showLoading) {
          this.errorSignal.set(error);
          this.statusSignal.set('error');
        }
        // Reload silencioso que falhou: a ação que o disparou já foi confirmada pelo
        // servidor: não há erro para mostrar, só a ordenação que fica desatualizada até o
        // próximo `load()`.
      },
    });
  }

  /** `true` se o item ainda pertence à visão dos filtros de estado e atraso carregados
   * (prioridade e busca não mudam ao concluir/reabrir, por isso não entram aqui). */
  private matchesStateFilters(task: TaskResponse): boolean {
    const { status, overdue } = this.filtersSignal();
    return (
      (status === 'all' || task.status === (status === 'pending' ? 'Pending' : 'Completed')) &&
      (overdue !== true || task.isOverdue)
    );
  }

  /**
   * Mecanismo comum de `complete`/`reopen`: aplica `optimisticPatch` de imediato (se o item
   * estiver na página carregada) e, se o item deixar de casar com os filtros de estado/atraso,
   * já o tira da lista (e de `totalCount`); chama a API e reconcilia com a resposta; em
   * sucesso, recarrega a página atual em silêncio (ver o comentário de classe); em erro,
   * reverte — para 404, removendo o item; para qualquer outro erro (incluindo 409),
   * restaurando o item exatamente como estava (no mesmo índice, se tinha saído).
   */
  private transition(
    id: string,
    optimisticPatch: (task: TaskResponse) => TaskResponse,
    call: () => Observable<TaskResponse>,
  ): Observable<TaskResponse> {
    const items = this.itemsSignal();
    const index = items.findIndex((item) => item.id === id);
    const previous = index === -1 ? null : items[index];

    let removedOptimistically = false;
    if (previous) {
      const patched = optimisticPatch(previous);
      if (this.matchesStateFilters(patched)) {
        this.itemsSignal.set(items.map((item) => (item.id === id ? patched : item)));
      } else {
        // O item deixou de casar com o filtro de estado/atraso: sai agora, não no reload (FE-19, CA-06).
        this.itemsSignal.set(items.filter((item) => item.id !== id));
        this.totalCountSignal.update((count) => Math.max(0, count - 1));
        removedOptimistically = true;
      }
    }

    return call().pipe(
      tap((serverTask) => {
        this.replaceItem(serverTask);
        this.fetchPage(this.pageSignal(), this.pageSizeSignal(), this.filtersSignal(), {
          showLoading: false,
        });
      }),
      catchError((error: AppError) => {
        if (error.status === 404) {
          this.dropItem(id);
        } else if (
          previous &&
          removedOptimistically &&
          // Um reload silencioso de outra ação pode já ter trazido o item de volta.
          !this.itemsSignal().some((item) => item.id === id)
        ) {
          // `replaceItem` é um `map` e não reinsere: devolve o item ao índice original.
          const restored = [...this.itemsSignal()];
          restored.splice(index, 0, previous);
          this.itemsSignal.set(restored);
          this.totalCountSignal.update((count) => count + 1);
        } else if (previous) {
          this.replaceItem(previous);
        }
        return throwError(() => error);
      }),
    );
  }
}
