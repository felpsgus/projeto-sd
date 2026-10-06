import { AbstractControl, ValidationErrors } from '@angular/forms';

/** Backend limits mirrored by the client validation (RN-TASK-02, RN-TASK-03). */
export const TITLE_MAX_LENGTH = 200;
export const DESCRIPTION_MAX_LENGTH = 2000;

/**
 * Título é obrigatório e não pode ser só espaços (RN-TASK-02) — `Validators.required`
 * sozinho não pega uma string de espaços, então a checagem é feita aqui, sobre o valor
 * já sem espaços nas pontas.
 */
export function titleValidator(control: AbstractControl<string>): ValidationErrors | null {
  const trimmed = (control.value ?? '').trim();
  if (trimmed.length === 0) {
    return { required: true };
  }
  if (trimmed.length > TITLE_MAX_LENGTH) {
    return { maxlength: { requiredLength: TITLE_MAX_LENGTH, actualLength: trimmed.length } };
  }
  return null;
}
