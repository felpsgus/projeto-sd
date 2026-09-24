import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Observable } from 'rxjs';

import { TasksStore } from '../tasks.store';
import { TaskItemComponent } from '../task-item/task-item.component';
import { LoadingComponent } from '../../../shared/ui/loading/loading.component';
import { EmptyStateComponent } from '../../../shared/ui/empty-state/empty-state.component';
import { ErrorStateComponent } from '../../../shared/ui/error-state/error-state.component';
import { TaskResponse } from '../../../core/api/models/task.models';
import { AppError } from '../../../core/errors/app-error.model';

/**
 * Listagem paginada de tarefas (FE-15): container que consome `TasksStore`, trata os três
 * estados (carregando/vazio/erro) com os componentes compartilhados e delega a apresentação
 * de cada item a `<app-task-item>`.
 *
 * **Estado por item, não por página (FE-18/19/20):** este componente mantém um conjunto de
 * ids "em voo" (`pendingIds`) e um mapa de erro por id (`itemErrors`) — uma ação sobre um
 * item nunca troca `store.status` nem recarrega a lista inteira (ver o comentário de classe
 * de `TasksStore`), então os demais itens não piscam nem perdem o próprio estado.
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
export class TasksPageComponent implements OnInit {
  protected readonly store = inject(TasksStore);
  private readonly router = inject(Router);

  private readonly pendingIdsSignal = signal<ReadonlySet<string>>(new Set());
  private readonly itemErrorsSignal = signal<Readonly<Record<string, string>>>({});

  ngOnInit(): void {
    this.store.load(1, this.store.pageSize());
  }

  protected retry(): void {
    this.store.load(this.store.page(), this.store.pageSize());
  }

  protected previousPage(): void {
    if (this.store.page() > 1) {
      this.store.load(this.store.page() - 1, this.store.pageSize());
    }
  }

  protected nextPage(): void {
    if (this.store.page() < this.store.totalPages()) {
      this.store.load(this.store.page() + 1, this.store.pageSize());
    }
  }

  protected goToCreate(): void {
    void this.router.navigateByUrl('/tasks/new');
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

    request.subscribe({
      next: () => {
        this.setPending(taskId, false);
        onSuccess?.();
      },
      error: (error: AppError) => {
        this.setPending(taskId, false);
        this.setError(taskId, error.message);
      },
    });
  }

  /**
   * Remover pode esvaziar a página atual (FE-20, CA-10/CA-11): se sobrou vazio e não é a
   * primeira página, recua uma página em vez de deixar uma lista vazia no meio da
   * paginação. Na página 1, o estado vazio (`store.isEmpty()`) já resolve sozinho.
   */
  private afterRemove(): void {
    if (this.store.items().length === 0 && this.store.page() > 1) {
      this.store.load(this.store.page() - 1, this.store.pageSize());
    }
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
