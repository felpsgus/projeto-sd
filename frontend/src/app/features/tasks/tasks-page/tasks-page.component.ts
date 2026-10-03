import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Observable, debounceTime, distinctUntilChanged } from 'rxjs';

import { TasksStore } from '../tasks.store';
import { TaskItemComponent } from '../task-item/task-item.component';
import { LoadingComponent } from '../../../shared/ui/loading/loading.component';
import { EmptyStateComponent } from '../../../shared/ui/empty-state/empty-state.component';
import { ErrorStateComponent } from '../../../shared/ui/error-state/error-state.component';
import { PRIORITY_FILTER_LABELS } from '../task-labels';
import {
  TaskPriorityFilter,
  TaskResponse,
  TaskStatusFilter,
  TasksFilters,
  hasActiveTaskFilters,
} from '../../../core/api/models/task.models';
import { AppError } from '../../../core/errors/app-error.model';
import { TasksQueryState, buildTasksQueryParams, parseTasksQueryParams } from './tasks-query.util';

const SEARCH_DEBOUNCE_MS = 300;

/**
 * Listagem, filtros e busca de tarefas (FE-15/FE-16): container que consome `TasksStore`,
 * trata os três estados (carregando/vazio/erro) com os componentes compartilhados e delega a
 * apresentação de cada item a `<app-task-item>`.
 *
 * **A URL é a única fonte de verdade dos filtros (FE-16, FD-08, CA-20):** este componente não
 * guarda página nem filtro em signal próprio — `queryState` é só a leitura saneada de
 * `ActivatedRoute.queryParamMap` (via `toSignal`). Um `effect()` observa `queryState()` e
 * chama `TasksStore.load()` sempre que ela mudar — por navegação do usuário, pelo botão
 * "voltar", por um link colado direto no navegador ou por `F5`. Mudar um filtro **navega**
 * (com `replaceUrl: true`, para não entupir o histórico a cada tecla ou clique — CA-19);
 * mudar de página navega normalmente (histórico completo).
 *
 * **Busca com debounce e sem corrida (CA-10/CA-11):** o campo de busca escreve num signal
 * local (`searchInputSignal`), não direto na URL — só depois de ~300 ms sem digitar é que o
 * valor vira navegação. A proteção contra respostas fora de ordem é a mesma de sempre:
 * `TasksStore.load()` usa o contador de requisição interno, então uma resposta antiga nunca
 * sobrescreve uma mais nova, mesmo se chegarem fora de ordem.
 *
 * **Estado por item, não por página (FE-18/19/20):** este componente mantém um conjunto de
 * ids "em voo" (`pendingIds`) e um mapa de erro por id (`itemErrors`) — uma ação sobre um
 * item nunca troca `store.status` nem o filtro atual.
 */
@Component({
  selector: 'app-tasks-page',
  imports: [
    RouterLink,
    TaskItemComponent,
    LoadingComponent,
    EmptyStateComponent,
    ErrorStateComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './tasks-page.component.html',
  styleUrl: './tasks-page.component.scss',
})
export class TasksPageComponent {
  protected readonly store = inject(TasksStore);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly priorityOptions: readonly TaskPriorityFilter[] = ['low', 'medium', 'high'];
  protected readonly priorityLabels = PRIORITY_FILTER_LABELS;

  private readonly queryParamMap = toSignal(this.route.queryParamMap, {
    initialValue: this.route.snapshot.queryParamMap,
  });

  /** Página + filtros saneados a partir da URL corrente (FE-16) — única fonte de verdade. */
  protected readonly queryState = computed<TasksQueryState>(() =>
    parseTasksQueryParams(this.queryParamMap()),
  );

  protected readonly hasActiveFilters = computed(() =>
    hasActiveTaskFilters(this.queryState().filters),
  );

  protected readonly resultsSummary = computed(() => {
    const total = this.store.totalCount();
    const base = total === 1 ? '1 tarefa encontrada' : `${total} tarefas encontradas`;
    return this.hasActiveFilters() ? `${base} com os filtros aplicados` : base;
  });

  /** Espelho local do campo de busca — só vira navegação depois do debounce (CA-10). */
  private readonly searchInputSignal = signal(
    this.route.snapshot.queryParamMap.get('search') ?? '',
  );
  protected readonly searchInputValue = this.searchInputSignal.asReadonly();

  private readonly pendingIdsSignal = signal<ReadonlySet<string>>(new Set());
  private readonly itemErrorsSignal = signal<Readonly<Record<string, string>>>({});
  /** Aviso de 404 (item já sumiu da lista): fica na página, não no item removido. */
  private readonly pageNoticeSignal = signal<string | null>(null);
  protected readonly pageNotice = this.pageNoticeSignal.asReadonly();

  constructor() {
    // Carrega a lista sempre que página/filtros da URL mudarem — deep link, F5, "voltar" e
    // navegação normal passam todos por aqui, sem lógica duplicada (FE-16, CA-15/CA-16).
    effect(() => {
      const state = this.queryState();
      this.pageNoticeSignal.set(null);
      this.store.load(state.page, this.store.pageSize(), state.filters);
    });

    // Mantém o campo de busca em sincronia quando a URL muda por fora da digitação (voltar,
    // avançar, deep link, F5) — sem isso, o campo ficaria com o texto de uma busca anterior.
    // Lê `searchInputSignal` sem criar dependência (senão o próprio `set` abaixo re-disparia
    // o efeito) — só reage a mudanças da URL.
    effect(() => {
      const urlSearch = this.queryState().filters.search;
      if (urlSearch !== untracked(this.searchInputSignal)) {
        this.searchInputSignal.set(urlSearch);
      }
    });

    // Debounce da busca (FE-16, CA-10): só navega ~300 ms depois da última tecla. Se o valor
    // já é o que está na URL (por exemplo, acabou de ser sincronizado pelo efeito acima),
    // não navega de novo.
    toObservable(this.searchInputSignal)
      .pipe(debounceTime(SEARCH_DEBOUNCE_MS), distinctUntilChanged(), takeUntilDestroyed())
      .subscribe((value) => {
        if (value === this.queryState().filters.search) {
          return;
        }
        this.applyFilters({ ...this.queryState().filters, search: value });
      });
  }

  protected retry(): void {
    const state = this.queryState();
    this.store.load(state.page, this.store.pageSize(), state.filters);
  }

  protected previousPage(): void {
    const state = this.queryState();
    if (state.page > 1) {
      this.goToPage(state.page - 1);
    }
  }

  protected nextPage(): void {
    const state = this.queryState();
    if (state.page < this.store.totalPages()) {
      this.goToPage(state.page + 1);
    }
  }

  protected goToCreate(): void {
    void this.router.navigateByUrl('/tasks/new');
  }

  protected isStatusSelected(status: TaskStatusFilter): boolean {
    return this.queryState().filters.status === status;
  }

  protected onStatusChange(status: TaskStatusFilter): void {
    this.applyFilters({ ...this.queryState().filters, status });
  }

  protected isPriorityChecked(priority: TaskPriorityFilter): boolean {
    return this.queryState().filters.priority.includes(priority);
  }

  protected onPriorityToggle(priority: TaskPriorityFilter): void {
    const current = this.queryState().filters.priority;
    const next = current.includes(priority)
      ? current.filter((value) => value !== priority)
      : [...current, priority];
    this.applyFilters({ ...this.queryState().filters, priority: next });
  }

  protected isOverdueChecked(): boolean {
    return this.queryState().filters.overdue === true;
  }

  protected onOverdueToggle(event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    this.applyFilters({ ...this.queryState().filters, overdue: checked ? true : null });
  }

  protected onSearchInput(event: Event): void {
    this.searchInputSignal.set((event.target as HTMLInputElement).value);
  }

  protected clearFilters(): void {
    this.searchInputSignal.set('');
    void this.router.navigate([], { relativeTo: this.route, queryParams: {}, replaceUrl: true });
  }

  protected isPending(taskId: string): boolean {
    return this.pendingIdsSignal().has(taskId);
  }

  protected errorFor(taskId: string): string | null {
    return this.itemErrorsSignal()[taskId] ?? null;
  }

  protected onComplete(task: TaskResponse): void {
    this.runItemAction(task.id, this.store.complete(task.id));
  }

  protected onReopen(task: TaskResponse): void {
    this.runItemAction(task.id, this.store.reopen(task.id));
  }

  protected onRemove(task: TaskResponse): void {
    this.runItemAction(task.id, this.store.remove(task.id), () => this.afterRemove());
  }

  /**
   * Guarda de pilha de pedidos (FE-19, CA-17): se o id já está em voo, ignora um novo
   * clique — nenhuma ação duplicada, e ids diferentes seguem em paralelo sem se atrapalhar
   * (CA-18).
   */
  private runItemAction<T>(taskId: string, request: Observable<T>, onSuccess?: () => void): void {
    if (this.isPending(taskId)) {
      return;
    }
    this.setPending(taskId, true);
    this.clearError(taskId);
    this.pageNoticeSignal.set(null);

    request.subscribe({
      next: () => {
        this.setPending(taskId, false);
        onSuccess?.();
      },
      error: (error: AppError) => {
        this.setPending(taskId, false);
        if (error.status === 404) {
          this.pageNoticeSignal.set(error.message);
        } else {
          this.setError(taskId, error.message);
        }
      },
    });
  }

  /**
   * Remover pode esvaziar a página atual (FE-20, CA-10/CA-11): se sobrou vazio e não é a
   * primeira página, recua uma página (navegando, para a URL continuar refletindo a
   * paginação real) em vez de deixar uma lista vazia no meio da paginação. Na página 1, o
   * estado vazio (`store.isEmpty()`/`isFilteredEmpty()`) já resolve sozinho.
   */
  private afterRemove(): void {
    const state = this.queryState();
    if (this.store.items().length === 0 && state.page > 1) {
      this.goToPage(state.page - 1);
    }
  }

  /** Muda de página navegando (histórico completo, ao contrário de um filtro — CA-19). */
  private goToPage(page: number): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: buildTasksQueryParams({ page, filters: this.queryState().filters }),
      replaceUrl: false,
    });
  }

  /** Aplica um novo conjunto de filtros: sempre volta para a página 1 (CA-21) e usa
   * `replaceUrl` para não entupir o histórico (CA-19). */
  private applyFilters(filters: TasksFilters): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: buildTasksQueryParams({ page: 1, filters }),
      replaceUrl: true,
    });
  }

  private setPending(taskId: string, pending: boolean): void {
    this.pendingIdsSignal.update((current) => {
      const next = new Set(current);
      if (pending) {
        next.add(taskId);
      } else {
        next.delete(taskId);
      }
      return next;
    });
  }

  private setError(taskId: string, message: string): void {
    this.itemErrorsSignal.update((current) => ({ ...current, [taskId]: message }));
  }

  private clearError(taskId: string): void {
    this.itemErrorsSignal.update((current) => {
      if (!(taskId in current)) {
        return current;
      }
      const next = { ...current };
      delete next[taskId];
      return next;
    });
  }
}
