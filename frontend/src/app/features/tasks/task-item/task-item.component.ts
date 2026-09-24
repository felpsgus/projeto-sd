import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { DatePipe } from '@angular/common';

import { TaskResponse } from '../../../core/api/models/task.models';
import { PRIORITY_LABELS, STATUS_LABELS } from '../task-labels';
import { formatDueDate } from '../task-date.util';

/**
 * Item de tarefa, de apresentação pura (FE-15, CA-25): recebe a tarefa por `input()`, não
 * injeta o store. Sem ações (editar/concluir/reabrir/remover ficam fora do T2).
 */
@Component({
  selector: 'app-task-item',
  imports: [DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <li class="task-item" [class.task-item--completed]="task().status === 'Completed'">
      <span
        class="task-item__rule"
        [class.task-item__rule--low]="task().priority === 'Low'"
        [class.task-item__rule--medium]="task().priority === 'Medium'"
        [class.task-item__rule--high]="task().priority === 'High'"
        aria-hidden="true"
      ></span>

      <div class="task-item__body">
        <div class="task-item__heading">
          <h3 class="task-item__title">{{ task().title }}</h3>

          <div
            class="task-item__due-group"
            [class.task-item__due-group--overdue]="task().isOverdue"
          >
            <p class="task-item__due tabular-nums">
              <span class="visually-hidden">Vencimento: </span>
              @if (task().isOverdue) {
                <span class="task-item__due-dot" aria-hidden="true"></span>
              }
              @if (dueDateLabel(); as dueDate) {
                {{ dueDate }}
              } @else {
                Sem vencimento
              }
            </p>

            @if (task().isOverdue) {
              <p class="task-item__overdue-label">Atrasada</p>
            }
          </div>
        </div>

        @if (task().description; as description) {
          <p class="task-item__description">{{ description }}</p>
        }

        <dl class="task-item__meta">
          <div class="task-item__meta-item">
            <dt class="visually-hidden">Prioridade</dt>
            <dd>{{ priorityLabel() }}</dd>
          </div>

          <div class="task-item__meta-item">
            <dt class="visually-hidden">Situação</dt>
            <dd>{{ statusLabel() }}</dd>
          </div>

          <div class="task-item__meta-item task-item__meta-item--updated">
            <dt class="visually-hidden">Última atualização</dt>
            <dd class="tabular-nums">
              Atualizada em {{ task().updatedAt | date: 'dd/MM/yyyy HH:mm' }}
            </dd>
          </div>
        </dl>
      </div>
    </li>
  `,
  styleUrl: './task-item.component.scss',
})
export class TaskItemComponent {
  readonly task = input.required<TaskResponse>();

  protected priorityLabel(): string {
    const priority = this.task().priority;
    return priority ? PRIORITY_LABELS[priority] : 'Sem prioridade';
  }

  protected statusLabel(): string {
    return STATUS_LABELS[this.task().status];
  }

  protected dueDateLabel(): string | null {
    return formatDueDate(this.task().dueDate);
  }
}
