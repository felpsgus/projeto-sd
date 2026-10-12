import { AbstractControl, ValidationErrors } from '@angular/forms';

/** Backend limit espelhado no cliente (RN-USER-02). */
export const DISPLAY_NAME_MAX_LENGTH = 100;

/**
 * Nome de exibição é obrigatório e não pode ser só espaços (RN-USER-02, FE-11 CA-08) —
 * igual ao raciocínio de `titleValidator` em `task-form.validators.ts`: `Validators.required`
 * sozinho não pega uma string de espaços.
 */
export function displayNameValidator(control: AbstractControl<string>): ValidationErrors | null {
  const trimmed = (control.value ?? '').trim();
  if (trimmed.length === 0) {
    return { required: true };
  }
  if (trimmed.length > DISPLAY_NAME_MAX_LENGTH) {
    return {
      maxlength: { requiredLength: DISPLAY_NAME_MAX_LENGTH, actualLength: trimmed.length },
    };
  }
  return null;
}
