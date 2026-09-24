import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { Observable, catchError, tap, throwError } from 'rxjs';

import { TasksApi } from '../../core/api/tasks-api.service';
import {
  CreateTaskRequest,
  TaskResponse,
  UpdateTaskRequest,
} from '../../core/api/models/task.models';
import { SessionStore } from '../../core/auth/session-store';
import { AppError } from '../../core/errors/app-error.model';

const DEFAULT_PAGE_SIZE = 20;

/** Estado assíncrono de carregamento da listagem (FE-14). */
export type TasksStatus = 'idle' | 'loading' | 'success' | 'error';

/**
 * Camada única de estado de tarefas (FE-14), exposta por signals: `load` (paginado),
 * `create`, `getById`, `update`, `complete`, `reopen` e `remove`. Nenhum componente injeta
 * `TasksApi` diretamente; sempre este store.
 *
 * **Como cada mutação reflete na lista (decisão de 23/09/2026, terceira onda da Fase 1):**
 * nenhuma delas recarrega a página inteira (`load`) — só troca o item afetado no array em
 * memória, ou o remove. Duas razões, válidas para `update`, `complete` e `reopen`:
 *
 * 1. A ordenação do T2 é só por `createdAt` decrescente ({@link TasksApi.list} espelha
 *    `ListTasksHandler`, que ainda não ordena por estado — isso é BE-22/Fase 2). Editar,
 *    concluir ou reabrir uma tarefa hoje nunca muda sua posição na página atual; recarregar
 *    a página não traria nenhuma correção de ordenação que valha o custo.
 * 2. `load()` põe `status` em `'loading'`, e `TasksPageComponent` usa isso para trocar a
 *    lista inteira por um spinner — exatamente o "piscar a cada ação" que este onda pede
 *    para evitar, e descartaria o estado (pendência/erro) dos itens não afetados pela ação.
 *
 * Se a Fase 2 mudar a ordenação para depender do estado, `complete`/`reopen` precisarão
 * voltar a recarregar a página — o comentário fica pelo motivo.
 *
 * `remove` é diferente: afeta `totalCount` e pode esvaziar a página atual, então quem
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

  /** Carrega uma página de tarefas (sem filtros — FE-16 fica para a Fase 2). */
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
   * ao estado anterior. Em sucesso, o item é substituído pelo que o servidor devolveu
   * (fonte da verdade para `completedAt`/`isOverdue`). Em 404, o item sai da lista
   * (RN-AUTZ-03) — ver o comentário de classe para o porquê de não recarregar a página.
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
   * Mecanismo comum de `complete`/`reopen`: aplica `optimisticPatch` de imediato (se o item
   * estiver na página carregada), chama a API e reconcilia com a resposta; em erro, reverte
   * — para 404, removendo o item; para qualquer outro erro (incluindo 409), restaurando o
   * item exatamente como estava antes do clique. Só o item afetado troca de referência no
   * array — os demais mantêm a mesma referência, então `@for` (por `track task.id`) não
   * re-renderiza o resto da lista.
   */
  private transition(
    id: string,
    optimisticPatch: (task: TaskResponse) => TaskResponse,
    call: () => Observable<TaskResponse>,
  ): Observable<TaskResponse> {
    const items = this.itemsSignal();
    const index = items.findIndex((item) => item.id === id);
    const previous = index === -1 ? null : items[index];

    if (previous) {
      this.itemsSignal.set(items.map((item) => (item.id === id ? optimisticPatch(item) : item)));
    }

    return call().pipe(
      tap((serverTask) => this.replaceItem(serverTask)),
      catchError((error: AppError) => {
        if (error.status === 404) {
          this.dropItem(id);
        } else if (previous) {
          this.replaceItem(previous);
        }
        return throwError(() => error);
      }),
    );
  }
}
