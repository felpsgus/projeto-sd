import {
  ChangeDetectionStrategy,
  Component,
  ContentChild,
  ElementRef,
  ViewChild,
  effect,
  input,
  output,
  signal,
} from '@angular/core';

let nextDialogId = 0;

/**
 * Diálogo de confirmação nativo e acessível (FE-04/FE-20, FD-15) — usado antes de qualquer
 * ação destrutiva (hoje só remover tarefa). Reutilizável: quem o usa passa título, mensagem
 * e rótulo do botão de confirmar, e chama `open()` a partir de um clique.
 *
 * **Por que `<dialog>` sem `showModal()`:** o elemento é nativo e semântico (papel
 * `alertdialog`, foco preso, `Esc` fecha), mas `showModal()`/`close()` não existem de fato
 * no jsdom usado pelos testes (`HTMLDialogElement` lá é um `HTMLElement` genérico — ver
 * `jsdom/lib/jsdom/living/nodes/HTMLDialogElement-impl.js`), então depender deles quebraria
 * todo teste de Testing Library que abre o diálogo. Em vez disso, a visibilidade é
 * controlada pelo atributo `open` (a regra `dialog:not([open]) { display: none }` do user
 * agent já esconde o elemento fechado em qualquer navegador real) e o foco/backdrop são
 * geridos aqui à mão — mesmo resultado de acessibilidade, sem depender de uma API que o
 * ambiente de teste não implementa.
 */
@Component({
  selector: 'app-confirm-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (isOpen()) {
      <div class="confirm-dialog__scrim"></div>
    }
    <dialog
      #dialogEl
      class="confirm-dialog"
      role="alertdialog"
      [attr.open]="isOpen() ? '' : null"
      [attr.aria-modal]="isOpen() ? 'true' : null"
      [attr.aria-labelledby]="titleId"
      [attr.aria-describedby]="messageId"
      (keydown)="onKeydown($event)"
    >
      @if (isOpen()) {
        <h2 [id]="titleId" class="confirm-dialog__title">{{ title() }}</h2>
        <p [id]="messageId" class="confirm-dialog__message">{{ message() }}</p>
        <ng-content></ng-content>
        @if (errorMessage(); as msg) {
          <p class="confirm-dialog__error" role="alert">{{ msg }}</p>
        }
        <div class="confirm-dialog__actions">
          <button
            #cancelBtn
            type="button"
            class="confirm-dialog__cancel"
            [disabled]="busy()"
            (click)="onCancel()"
          >
            Cancelar
          </button>
          <button
            #confirmBtn
            type="button"
            class="confirm-dialog__confirm"
            [disabled]="busy() || confirmDisabled()"
            [attr.aria-busy]="busy()"
            (click)="onConfirm()"
          >
            {{ confirmLabel() }}
          </button>
        </div>
      }
    </dialog>
  `,
  styleUrl: './confirm-dialog.component.scss',
})
export class ConfirmDialogComponent {
  readonly title = input.required<string>();
  readonly message = input.required<string>();
  readonly confirmLabel = input('Confirmar');
  /** Enquanto `true`, os dois botões ficam desabilitados (requisição em voo, CA-14 de FE-20). */
  readonly busy = input(false);
  /** Desabilita só o confirmar, além de `busy` — usado quando um campo projetado (ex.: senha) está inválido (FE-13, CA-04). */
  readonly confirmDisabled = input(false);
  /** Mensagem de erro exibida dentro do diálogo (ex.: senha incorreta, FE-13 CA-16) — nunca fecha o diálogo sozinha. */
  readonly errorMessage = input<string | null>(null);
  /**
   * Quando `true`, o diálogo **não** fecha sozinho ao `busy` voltar a `false` — quem o usa
   * decide quando fechar, chamando `close()`. Necessário quando um erro deve manter o
   * diálogo aberto (FE-13, CA-16/CA-17) em vez do fechamento automático de FE-20.
   */
  readonly preventAutoClose = input(false);

  /** Emitido ao clicar em confirmar — quem escuta decide se/quando o diálogo fecha (via `busy`/`close()`). */
  readonly confirmed = output<void>();
  /** Emitido ao cancelar (clique, `Esc` ou clique fora) — o diálogo já fecha sozinho. */
  readonly cancelled = output<void>();

  protected readonly isOpen = signal(false);
  protected readonly titleId = `confirm-dialog-title-${++nextDialogId}`;
  protected readonly messageId = `confirm-dialog-message-${nextDialogId}`;

  @ViewChild('cancelBtn') private readonly cancelBtnRef?: ElementRef<HTMLButtonElement>;
  @ViewChild('confirmBtn') private readonly confirmBtnRef?: ElementRef<HTMLButtonElement>;
  @ViewChild('dialogEl') private readonly dialogElRef?: ElementRef<HTMLElement>;
  /** Elemento projetado marcado com `#autofocusTarget` (ex.: campo de senha) que recebe o
   * foco inicial em vez do botão cancelar (FE-13, CA-05). */
  @ContentChild('autofocusTarget') private readonly autofocusTarget?: ElementRef<HTMLElement>;

  private previouslyFocused: HTMLElement | null = null;
  private wasBusy = false;

  constructor() {
    // A requisição que `confirmed` disparou terminou (sucesso ou falha) — por padrão o
    // diálogo fecha sozinho (FE-20: um eventual erro aparece na linha da lista, não aqui).
    // `preventAutoClose` desliga isto para os casos em que o próprio diálogo deve mostrar o
    // erro e continuar aberto (FE-13).
    effect(() => {
      const busy = this.busy();
      if (this.wasBusy && !busy && !this.preventAutoClose()) {
        this.close();
      }
      this.wasBusy = busy;
    });
  }

  open(): void {
    if (this.isOpen()) {
      return;
    }
    this.previouslyFocused = document.activeElement as HTMLElement | null;
    this.isOpen.set(true);
    queueMicrotask(() => {
      const target = this.autofocusTarget?.nativeElement ?? this.cancelBtnRef?.nativeElement;
      target?.focus();
    });
  }

  /** Fecha o diálogo. Público para quem usa `preventAutoClose` decidir o momento (FE-13). */
  close(): void {
    if (!this.isOpen()) {
      return;
    }
    this.isOpen.set(false);
    const toFocus = this.previouslyFocused;
    this.previouslyFocused = null;
    queueMicrotask(() => toFocus?.focus());
  }

  protected onCancel(): void {
    this.cancelled.emit();
    this.close();
  }

  protected onConfirm(): void {
    if (this.confirmDisabled()) {
      return;
    }
    this.confirmed.emit();
  }

  protected onKeydown(event: KeyboardEvent): void {
    if (!this.isOpen()) {
      return;
    }
    if (event.key === 'Escape') {
      event.preventDefault();
      this.onCancel();
      return;
    }
    if (event.key !== 'Tab') {
      return;
    }
    const focusables = this.focusableElements();
    const first = focusables[0];
    const last = focusables.at(-1);
    if (!first || !last) {
      return;
    }
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  }

  /**
   * Elementos focáveis dentro do diálogo, calculados a cada `Tab` (não só os dois botões
   * fixos) — necessário porque um conteúdo projetado (ex.: campo de senha de FE-13) entra
   * no laço de foco (CA-09).
   */
  private focusableElements(): HTMLElement[] {
    const root = this.dialogElRef?.nativeElement;
    if (!root) {
      return [];
    }
    return Array.from(
      root.querySelectorAll<HTMLElement>(
        'input:not([disabled]), button:not([disabled]), select:not([disabled]), textarea:not([disabled]), a[href]',
      ),
    );
  }
}
