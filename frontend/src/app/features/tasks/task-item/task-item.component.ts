import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  ViewChild,
  effect,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';

import { TaskResponse, TaskStatus } from '../../../core/api/models/task.models';
import { PRIORITY_LABELS, STATUS_LABELS } from '../task-labels';
import { formatDueDate } from '../task-date.util';
import { ConfirmDialogComponent } from '../../../shared/ui/confirm-dialog/confirm-dialog.component';

/**
 * Item de tarefa (FE-15/FE-18/FE-19/FE-20): apresentação com ações na própria linha —
 * editar (link), concluir/reabrir (`complete`/`reopen`) e remover (`remove`, atrás de
 * confirmação). Continua recebendo `task` por `input()` e não injeta `TasksStore`: quem
 * chama a API é o container (`TasksPageComponent`), que também controla `pending` e
 * `actionError` — este componente só sabe pedir a ação e mostrar o que o container decidiu.
 *
 * **Otimismo é decisão do store, não daqui** (FD-06): quando `task()` muda de estado, este
 * componente já está exibindo o que `TasksStore` colocou em memória (otimista ou
 * reconciliado) — não há estado de conclusão duplicado aqui.
 */
@Component({
  selector: 'app-task-item',
  // O host é o item da lista (<ul role="list"> > app-task-item): sem <li> solto dentro de um
  // elemento customizado (axe `listitem`, FE-21 CA-01).
  host: { role: 'listitem', class: 'task-item-host' },
  imports: [DatePipe, RouterLink, ConfirmDialogComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './task-item.component.html',
  styleUrl: './task-item.component.scss',
})
export class TaskItemComponent {
  readonly task = input.required<TaskResponse>();
  /** `true` enquanto uma ação (concluir/reabrir/remover) está em voo para esta tarefa. */
  readonly pending = input(false);
  /** Mensagem de erro da última ação sobre esta tarefa, se houver (mantida pelo container). */
  readonly actionError = input<string | null>(null);

  readonly complete = output<void>();
  readonly reopen = output<void>();
  readonly remove = output<void>();

  @ViewChild(ConfirmDialogComponent) private readonly removeDialog?: ConfirmDialogComponent;

  private readonly title = viewChild.required<ElementRef<HTMLElement>>('title');

  protected readonly liveMessage = signal('');

  private previousStatus: TaskStatus | null = null;

  constructor() {
    // Anuncia a mudança de estado a leitor de tela (FE-19, CA-20) — só quando o status
    // efetivamente mudar (não no primeiro render, que não é uma "mudança").
    effect(() => {
      const current = this.task().status;
      if (this.previousStatus !== null && this.previousStatus !== current) {
        this.liveMessage.set(current === 'Completed' ? 'Tarefa concluída.' : 'Tarefa reaberta.');
      }
      this.previousStatus = current;
    });
  }

  /** Move o foco para o título — alvo quando o item vizinho sai da lista (FE-20, CA-20). */
  focusTitle(): void {
    this.title().nativeElement.focus();
  }

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

  protected toggleLabel(): string {
    return this.task().status === 'Completed' ? 'Reabrir' : 'Concluir';
  }

  protected toggleAriaLabel(): string {
    const prefix = this.task().status === 'Completed' ? 'Reabrir' : 'Concluir';
    return `${prefix}: ${this.task().title}`;
  }

  protected removeAriaLabel(): string {
    return `Remover: ${this.task().title}`;
  }

  protected removeConfirmMessage(): string {
    return `A tarefa "${this.task().title}" será removida definitivamente. Esta ação não pode ser desfeita.`;
  }

  protected onToggle(): void {
    if (this.pending()) {
      return;
    }
    if (this.task().status === 'Completed') {
      this.reopen.emit();
    } else {
      this.complete.emit();
    }
  }

  protected openRemoveDialog(): void {
    if (this.pending()) {
      return;
    }
    this.removeDialog?.open();
  }

  protected onRemoveConfirmed(): void {
    this.remove.emit();
  }
}
