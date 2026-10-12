import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * Exibe as mensagens de `fieldErrors` (FE-03) junto ao input correspondente, associadas
 * via `aria-describedby` no elemento que a usa (FE-04, CA-13) — este componente só
 * renderiza a lista; a associação do `id` ao `aria-describedby` do input é feita por quem
 * o consome.
 */
@Component({
  selector: 'app-form-field-error',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (messages().length > 0) {
      <ul [id]="fieldId()" class="form-field-error">
        @for (message of messages(); track message) {
          <li>{{ message }}</li>
        }
      </ul>
    }
  `,
  styleUrl: './form-field-error.component.scss',
})
export class FormFieldErrorComponent {
  readonly fieldId = input.required<string>();
  readonly messages = input<readonly string[]>([]);
}
