import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

/**
 * Estado vazio reutilizável (FE-03/FE-04). Componente de apresentação puro: recebe a
 * mensagem e o rótulo da ação por `input()`, emite a intenção por `output()` — quem
 * decide o que fazer é o container.
 */
@Component({
  selector: 'app-empty-state',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="empty-state">
      <p>{{ message() }}</p>
      @if (actionLabel()) {
        <button type="button" class="empty-state__action" (click)="action.emit()">
          {{ actionLabel() }}
        </button>
      }
    </div>
  `,
  styleUrl: './empty-state.component.scss',
})
export class EmptyStateComponent {
  readonly message = input.required<string>();
  readonly actionLabel = input<string>();
  readonly action = output<void>();
}
