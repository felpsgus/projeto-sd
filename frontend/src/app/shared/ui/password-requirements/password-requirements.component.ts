import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

import { evaluatePasswordRequirements } from '../../forms/password-policy';

/**
 * Indicador ao vivo dos três critérios de RN-AUTH-04 (FE-08 CA-06, reaproveitado por FE-12
 * CA-08 — **o mesmo componente**, não uma cópia). Recebe o valor atual da senha por
 * `input()` e não conhece formulário nem validador — a fonte da verdade dos critérios é
 * `evaluatePasswordRequirements`, a mesma função usada pelo validador de campo, para os
 * dois nunca divergirem.
 *
 * Nada de "força de senha" genérica: cada critério aparece com seu próprio texto e estado
 * (atendido/pendente), exatamente o que RN-AUTH-04 exige.
 */
@Component({
  selector: 'app-password-requirements',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ul class="password-requirements" aria-live="polite">
      <li [class.password-requirements__met]="status().minLength">
        <span aria-hidden="true">{{ status().minLength ? '✓' : '○' }}</span>
        Pelo menos 8 caracteres
        <span class="visually-hidden">{{ status().minLength ? '(atendido)' : '(pendente)' }}</span>
      </li>
      <li [class.password-requirements__met]="status().hasLetter">
        <span aria-hidden="true">{{ status().hasLetter ? '✓' : '○' }}</span>
        Pelo menos uma letra
        <span class="visually-hidden">{{ status().hasLetter ? '(atendido)' : '(pendente)' }}</span>
      </li>
      <li [class.password-requirements__met]="status().hasNumber">
        <span aria-hidden="true">{{ status().hasNumber ? '✓' : '○' }}</span>
        Pelo menos um número
        <span class="visually-hidden">{{ status().hasNumber ? '(atendido)' : '(pendente)' }}</span>
      </li>
    </ul>
  `,
  styleUrl: './password-requirements.component.scss',
})
export class PasswordRequirementsComponent {
  readonly password = input.required<string>();

  protected readonly status = computed(() => evaluatePasswordRequirements(this.password()));
}
