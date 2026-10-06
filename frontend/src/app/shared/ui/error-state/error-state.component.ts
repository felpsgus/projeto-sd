import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

/**
 * Estado de erro reutilizável, com ação de "tentar novamente" (FE-03, CA-10). Componente
 * de apresentação puro: quem re-executa a operação é o container que escuta `retry`.
 */
@Component({
  selector: 'app-error-state',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="error-state" role="alert">
      <p>{{ message() }}</p>
      <button type="button" class="error-state__retry" (click)="retry.emit()">
        Tentar novamente
      </button>
    </div>
  `,
  styleUrl: './error-state.component.scss',
})
export class ErrorStateComponent {
  readonly message = input.required<string>();
  readonly retry = output<void>();
}
