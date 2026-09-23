import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { Observable } from 'rxjs';

import { TasksApi } from '../../core/api/tasks-api.service';
import { CreateTaskRequest, TaskResponse } from '../../core/api/models/task.models';
import { SessionStore } from '../../core/auth/session-store';
import { AppError } from '../../core/errors/app-error.model';

const DEFAULT_PAGE_SIZE = 20;

/** Estado assíncrono de carregamento da listagem (FE-14). */
export type TasksStatus = 'idle' | 'loading' | 'success' | 'error';

/**
 * Camada única de estado de tarefas (FE-14, recorte parcial do T2), exposta por signals.
 *
 * Só três operações no T2: `load` (paginado, sem filtros), `create` e `getById` — sem
 * `update`, `complete`, `reopen` ou `remove`, que chamariam rotas que o Gateway não expõe
 * neste recorte (ver `features/tasks/README.md`).
 *
 * Nenhum componente injeta `TasksApi` diretamente; sempre este store.
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

  /** Contador de requisições em voo — protege contra respostas de `load` fora de ordem. */
  private latestRequestId = 0;

  readonly items = this.itemsSignal.asReadonly();
  readonly page = this.pageSignal.asReadonly();
  readonly pageSize = this.pageSizeSignal.asReadonly();
  readonly totalCount = this.totalCountSignal.asReadonly();
  readonly status = this.statusSignal.asReadonly();
  readonly error = this.errorSignal.asReadonly();

  /** Calculado no cliente a partir de `totalCount` — o backend não devolve `totalPages`. */
  readonly totalPages = computed(() =>
    Math.max(1, Math.ceil(this.totalCountSignal() / this.pageSizeSignal())),
  );

  /** `true` só em sucesso com zero itens — nunca durante carregamento ou erro. */
  readonly isEmpty = computed(
    () => this.statusSignal() === 'success' && this.itemsSignal().length === 0,
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

  /** Carrega uma página de tarefas (sem filtros, fora do recorte do T2 — ver FE-16). */
  load(page = 1, pageSize = this.pageSizeSignal()): void {
    this.statusSignal.set('loading');
    this.errorSignal.set(null);
    const requestId = ++this.latestRequestId;

    this.tasksApi.list({ page, pageSize }).subscribe({
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
        this.errorSignal.set(error);
        this.statusSignal.set('error');
      },
    });
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

  /** Esvazia o estado — chamado internamente ao encerrar a sessão. */
  clear(): void {
    this.itemsSignal.set([]);
    this.pageSignal.set(1);
    this.pageSizeSignal.set(DEFAULT_PAGE_SIZE);
    this.totalCountSignal.set(0);
    this.statusSignal.set('idle');
    this.errorSignal.set(null);
  }
}
