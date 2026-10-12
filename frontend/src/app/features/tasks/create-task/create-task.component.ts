import { ChangeDetectionStrategy, Component, ViewChild, inject } from '@angular/core';
import { Router } from '@angular/router';

import { TasksStore } from '../tasks.store';
import { TaskFormComponent, TaskFormValue } from '../task-form/task-form.component';
import { AppError } from '../../../core/errors/app-error.model';
import { ConfirmDialogComponent } from '../../../shared/ui/confirm-dialog/confirm-dialog.component';
import { CanComponentDeactivate } from '../edit-task/unsaved-changes.guard';

/**
 * Container de criação de tarefa (FE-17): delega apresentação e validação a
 * `<app-task-form>` (compartilhado com a edição, FE-18) e só decide o que fazer com o
 * valor emitido — chama `TasksStore.create` (`POST`) e, em sucesso (201), volta para
 * `/tasks`, que recarrega a lista do servidor (o cliente não sabe a posição da tarefa nova
 * na ordenação — RN-LIST-06 é decidida pelo backend).
 */
@Component({
  selector: 'app-create-task',
  imports: [TaskFormComponent, ConfirmDialogComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './create-task.component.html',
  styleUrl: './create-task.component.scss',
})
export class CreateTaskComponent implements CanComponentDeactivate {
  private readonly tasksStore = inject(TasksStore);
  private readonly router = inject(Router);

  @ViewChild(TaskFormComponent) private readonly taskForm?: TaskFormComponent;

  @ViewChild(ConfirmDialogComponent) private readonly leaveDialog?: ConfirmDialogComponent;
  private pendingLeave: Promise<boolean> | null = null;
  private resolveLeave?: (leave: boolean) => void;

  /** Após criar com sucesso, a saída da rota não deve pedir confirmação (FE-17, CA-19). */
  private savedSuccessfully = false;

  protected onSave(value: TaskFormValue): void {
    this.tasksStore.create(value).subscribe({
      next: () => {
        this.savedSuccessfully = true;
        void this.router.navigateByUrl('/tasks', { state: { notice: 'Tarefa criada.' } });
      },
      error: (error: AppError) => {
        this.taskForm?.submitFailed(error);
      },
    });
  }

  protected cancel(): void {
    void this.router.navigateByUrl('/tasks');
  }

  /**
   * Consultado pela `unsavedChangesGuard` ao tentar sair da rota. Com alterações abre o
   * diálogo e resolve com a resposta; uma segunda tentativa com o diálogo aberto reaproveita
   * a mesma promessa (não empilha outro diálogo).
   */
  canDeactivate(): boolean | Promise<boolean> {
    if (this.savedSuccessfully || !this.taskForm?.dirty) {
      return true;
    }
    this.pendingLeave ??= new Promise<boolean>((resolve) => {
      this.resolveLeave = resolve;
      this.leaveDialog?.open();
    });
    return this.pendingLeave;
  }

  protected answerLeave(leave: boolean): void {
    if (leave) {
      this.leaveDialog?.close();
    }
    this.resolveLeave?.(leave);
    this.pendingLeave = null;
  }
}
