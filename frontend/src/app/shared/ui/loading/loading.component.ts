import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * Indicador de carregamento reutilizável (FE-03). Componente de apresentação puro:
 * recebe por `input()`, não injeta serviço de dados (convenção 2.2).
 */
@Component({
  selector: 'app-loading',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p class="loading" role="status" aria-live="polite">
      <span class="loading__spinner" aria-hidden="true"></span>
      {{ label() }}
    </p>
  `,
  styleUrl: './loading.component.scss',
})
export class LoadingComponent {
  readonly label = input('Carregando…');
}
