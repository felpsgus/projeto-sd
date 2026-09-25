import {
  ChangeDetectionStrategy,
  Component,
  ViewChild,
  computed,
  input,
  output,
} from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';

import { ConfirmDialogComponent } from '../../../shared/ui/confirm-dialog/confirm-dialog.component';

/**
 * Diálogo de exclusão de conta (FE-13) — envolve `<app-confirm-dialog>` (FE-04) projetando
 * o campo de confirmação de senha (D-19 do backend) no seu `<ng-content>`.
 *
 * **Por que não é `confirm()` do navegador:** não é estilizável, não tem campo de senha e
 * o comportamento com leitor de tela é inconsistente (notas técnicas de FE-13).
 *
 * **`preventAutoClose`:** ao contrário da remoção de tarefa (FE-20), um erro aqui (senha
 * incorreta) precisa manter o diálogo aberto com a mensagem dentro dele (CA-16/CA-17) — por
 * isso o container (`AccountComponent`) controla `close()` explicitamente no sucesso.
 */
@Component({
  selector: 'app-delete-account-dialog',
  imports: [ConfirmDialogComponent, ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-confirm-dialog
      #dialog
      title="Excluir sua conta"
      [message]="message()"
      confirmLabel="Excluir permanentemente"
      [busy]="busy()"
      [confirmDisabled]="passwordControl.invalid"
      [errorMessage]="errorMessage()"
      [preventAutoClose]="true"
      (confirmed)="onConfirm()"
      (cancelled)="onCancelled()"
    >
      <div class="delete-account-dialog__field">
        <label for="delete-account-password">Confirme sua senha</label>
        <input
          #autofocusTarget
          id="delete-account-password"
          type="password"
          autocomplete="current-password"
          [formControl]="passwordControl"
        />
      </div>
    </app-confirm-dialog>
  `,
  styleUrl: './delete-account-dialog.component.scss',
})
export class DeleteAccountDialogComponent {
  /** Contagem de tarefas já carregada (`TasksStore`) — `null` se ainda não conhecida, para
   * não disparar uma chamada só para popular este texto (FE-13, notas técnicas). */
  readonly taskCount = input<number | null>(null);
  readonly busy = input(false);
  /** Mensagem de senha incorreta ou erro de rede vinda do container (CA-16). */
  readonly errorMessage = input<string | null>(null);

  /** Emitido com a senha digitada ao confirmar (senha só sai daqui — nunca persistida). */
  readonly confirmed = output<string>();
  readonly cancelled = output<void>();

  @ViewChild('dialog') private readonly dialogRef?: ConfirmDialogComponent;

  protected readonly passwordControl = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required],
  });

  protected readonly message = computed(() => {
    const count = this.taskCount();
    const taskSentence =
      count === null
        ? 'Todas as suas tarefas serão apagadas permanentemente.'
        : `Todas as suas ${count} tarefa${count === 1 ? '' : 's'} serão apagadas permanentemente.`;
    return `Esta ação é permanente e irreversível. ${taskSentence} Todas as sessões serão encerradas.`;
  });

  open(): void {
    this.passwordControl.reset('');
    this.dialogRef?.open();
  }

  /** Chamado pelo container após o 204 de sucesso (o próprio diálogo não decide fechar sozinho). */
  close(): void {
    this.dialogRef?.close();
  }

  protected onConfirm(): void {
    if (this.passwordControl.invalid) {
      return;
    }
    this.confirmed.emit(this.passwordControl.value);
  }

  protected onCancelled(): void {
    this.passwordControl.reset('');
    this.cancelled.emit();
  }
}
