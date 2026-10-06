import { ChangeDetectionStrategy, Component, ViewChild, inject } from '@angular/core';
import { Router } from '@angular/router';

import { TasksStore } from '../tasks.store';
import { TaskFormComponent, TaskFormValue } from '../task-form/task-form.component';
import { AppError } from '../../../core/errors/app-error.model';
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
  imports: [TaskFormComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './create-task.component.html',
  styleUrl: './create-task.component.scss',
})
export class CreateTaskComponent implements CanComponentDeactivate {
  private readonly tasksStore = inject(TasksStore);
  private readonly router = inject(Router);

  @ViewChild(TaskFormComponent) private readonly taskForm?: TaskFormComponent;

  /** Após criar com sucesso, a saída da rota não deve pedir confirmação (FE-17, CA-19). */
  private savedSuccessfully = false;

  protected onSave(value: TaskFormValue): void {
    this.tasksStore.create(value).subscribe({
      next: () => {
        this.savedSuccessfully = true;
        void this.router.navigateByUrl('/tasks');
      },
      error: (error: AppError) => {
        this.taskForm?.submitFailed(error);
      },
    });
  }

  protected cancel(): void {
    void this.router.navigateByUrl('/tasks');
  }

  /** Consultado pela `unsavedChangesGuard` (FE-17, CA-18/CA-19) ao tentar sair da rota. */
  canDeactivate(): boolean {
    if (this.savedSuccessfully || !this.taskForm?.dirty) {
      return true;
    }
    return window.confirm('Existem alterações não salvas. Deseja sair sem salvar?');
  }
}
