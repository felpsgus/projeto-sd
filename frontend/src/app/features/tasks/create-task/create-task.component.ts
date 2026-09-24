import { ChangeDetectionStrategy, Component, ViewChild, inject } from '@angular/core';
import { Router } from '@angular/router';

import { TasksStore } from '../tasks.store';
import { TaskFormComponent, TaskFormValue } from '../task-form/task-form.component';
import { AppError } from '../../../core/errors/app-error.model';

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
export class CreateTaskComponent {
  private readonly tasksStore = inject(TasksStore);
  private readonly router = inject(Router);

  @ViewChild(TaskFormComponent) private readonly taskForm?: TaskFormComponent;

  protected onSave(value: TaskFormValue): void {
    this.tasksStore.create(value).subscribe({
      next: () => {
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
}
