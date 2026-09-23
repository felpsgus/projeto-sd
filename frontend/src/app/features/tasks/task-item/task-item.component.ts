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
      <div class="task-item__main">
        <h3 class="task-item__title">{{ task().title }}</h3>
        @if (task().description; as description) {
          <p class="task-item__description">{{ description }}</p>
        }
      </div>

      <dl class="task-item__meta">
        <div class="task-item__badge task-item__badge--priority">
          <dt class="visually-hidden">Prioridade</dt>
          <dd>{{ priorityLabel() }}</dd>
        </div>

        <div class="task-item__badge task-item__badge--status">
          <dt class="visually-hidden">Situação</dt>
          <dd>{{ statusLabel() }}</dd>
        </div>

        @if (dueDateLabel(); as dueDate) {
          <div class="task-item__badge task-item__badge--due">
            <dt class="visually-hidden">Vencimento</dt>
            <dd>Vence em {{ dueDate }}</dd>
          </div>
        } @else {
          <div class="task-item__badge task-item__badge--due">
            <dt class="visually-hidden">Vencimento</dt>
            <dd>Sem vencimento</dd>
          </div>
        }

        @if (task().isOverdue) {
          <div class="task-item__badge task-item__badge--overdue">
            <dt class="visually-hidden">Alerta</dt>
            <dd>Atrasada</dd>
          </div>
        }
      </dl>

      <p class="task-item__updated">
        Atualizada em {{ task().updatedAt | date: 'dd/MM/yyyy HH:mm' }}
      </p>
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
