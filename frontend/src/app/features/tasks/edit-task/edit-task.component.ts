import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  ViewChild,
  computed,
  inject,
  signal,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { TasksStore } from '../tasks.store';
import { TaskFormComponent, TaskFormValue } from '../task-form/task-form.component';
import { LoadingComponent } from '../../../shared/ui/loading/loading.component';
import { ErrorStateComponent } from '../../../shared/ui/error-state/error-state.component';
import { STATUS_LABELS } from '../task-labels';
import { TaskResponse } from '../../../core/api/models/task.models';
import { AppError } from '../../../core/errors/app-error.model';
import { CanComponentDeactivate } from './unsaved-changes.guard';

type EditTaskStatus = 'loading' | 'ready' | 'not-found' | 'error';

const GUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/**
 * Container de edição de tarefa (FE-18), rota `/tasks/:id/edit`. Carrega a tarefa por
 * `GET /api/tasks/{id}` e reaproveita `<app-task-form>` (mesmo componente de FE-17) para
 * apresentação e validação — só título/rótulo do botão mudam.
 *
 * **A armadilha do `PUT` (RN-TASK-11, BE-19):** o backend trata `PUT` como substituição
 * total — um campo ausente no corpo apaga o valor atual. Por isso o formulário só é
 * renderizado depois que a tarefa carrega (nunca com valores parciais/`null` na tela — CA-02
 * de FE-18) e `onSave` sempre envia os quatro campos que `TaskFormValue` já garante
 * completos (a validação client-side não deixa nenhum "esquecido").
 *
 * **RN-AUTZ-03 no cliente:** um 404 — id inexistente, tarefa de outro usuário ou já
 * removida — leva à mesma tela "Tarefa não encontrada", sem nenhuma distinção de mensagem
 * (CA-17/CA-18/CA-19). Um id em formato inválido na URL (`/tasks/abc/edit`) cai na mesma
 * tela sem round-trip ao backend (CA-21).
 */
@Component({
  selector: 'app-edit-task',
  imports: [TaskFormComponent, LoadingComponent, ErrorStateComponent, RouterLink, DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './edit-task.component.html',
  styleUrl: './edit-task.component.scss',
})
export class EditTaskComponent implements OnInit, CanComponentDeactivate {
  private readonly route = inject(ActivatedRoute);
  private readonly tasksStore = inject(TasksStore);
  private readonly router = inject(Router);

  @ViewChild(TaskFormComponent) private readonly taskForm?: TaskFormComponent;

  protected readonly status = signal<EditTaskStatus>('loading');
  protected readonly task = signal<TaskResponse | null>(null);
  protected readonly statusLabels = STATUS_LABELS;

  protected readonly initialValue = computed<TaskFormValue | null>(() => {
    const current = this.task();
    if (!current) {
      return null;
    }
    return {
      title: current.title,
      description: current.description,
      priority: current.priority ?? 'Medium',
      dueDate: current.dueDate,
    };
  });

  private taskId: string | null = null;
  /** Após salvar com sucesso, a saída da rota não deve pedir confirmação (FE-18, CA-23). */
  private savedSuccessfully = false;

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id || !GUID_PATTERN.test(id)) {
      this.status.set('not-found');
      return;
    }
    this.taskId = id;
    this.loadTask(id);
  }

  protected retry(): void {
    if (this.taskId) {
      this.loadTask(this.taskId);
    }
  }

  protected onSave(value: TaskFormValue): void {
    if (!this.taskId) {
      return;
    }
    this.tasksStore.update(this.taskId, value).subscribe({
      next: () => {
        this.savedSuccessfully = true;
        void this.router.navigateByUrl('/tasks');
      },
      error: (error: AppError) => {
        if (error.status === 404) {
          // A tarefa foi removida (ou deixou de ser do usuário) enquanto era editada
          // (CA-22): a mesma tela de "não encontrada" resolve, sem travar a UI.
          this.status.set('not-found');
          return;
        }
        this.taskForm?.submitFailed(error);
      },
    });
  }

  protected cancel(): void {
    void this.router.navigateByUrl('/tasks');
  }

  /** Consultado pela `unsavedChangesGuard` (FE-18, CA-23) ao tentar sair da rota. */
  canDeactivate(): boolean {
    if (this.savedSuccessfully || this.status() !== 'ready') {
      return true;
    }
    if (!this.taskForm?.dirty) {
      return true;
    }
    return window.confirm('Existem alterações não salvas. Deseja sair sem salvar?');
  }

  private loadTask(id: string): void {
    this.status.set('loading');
    this.tasksStore.getById(id).subscribe({
      next: (task) => {
        this.task.set(task);
        this.status.set('ready');
      },
      error: (error: AppError) => {
        this.status.set(error.status === 404 ? 'not-found' : 'error');
      },
    });
  }
}
