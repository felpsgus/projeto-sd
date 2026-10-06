import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';

/** Espelha RN-AUTH-04 no cliente (FD-14) — o backend continua sendo a autoridade. */
export const PASSWORD_MIN_LENGTH = 8;

/** Estado de cada critério de RN-AUTH-04, usado pelo indicador ao vivo (FE-08/FE-12). */
export interface PasswordRequirementStatus {
  readonly minLength: boolean;
  readonly hasLetter: boolean;
  readonly hasNumber: boolean;
}

/** `true` só quando os três critérios de RN-AUTH-04 estão satisfeitos. */
export function isPasswordValid(status: PasswordRequirementStatus): boolean {
  return status.minLength && status.hasLetter && status.hasNumber;
}

/**
 * Avalia os três critérios de RN-AUTH-04 sobre um valor de senha — função pura,
 * compartilhada entre o validador de formulário e `<app-password-requirements>`, para os
 * dois nunca divergirem (FE-12, notas técnicas: "duplicá-lo garante que os dois vão
 * divergir").
 */
export function evaluatePasswordRequirements(value: string): PasswordRequirementStatus {
  return {
    minLength: value.length >= PASSWORD_MIN_LENGTH,
    hasLetter: /[a-zA-Z]/.test(value),
    hasNumber: /[0-9]/.test(value),
  };
}

/** Validador de campo único: RN-AUTH-04 (FE-08 CA-05, FE-12 CA-07). */
export function passwordPolicyValidator(control: AbstractControl<string>): ValidationErrors | null {
  const status = evaluatePasswordRequirements(control.value ?? '');
  return isPasswordValid(status) ? null : { passwordPolicy: status };
}

/**
 * Validador de grupo: dois campos devem ter o mesmo valor (FE-08 "confirmar senha", FE-12
 * "confirmar nova senha") — proteção de UX contra erro de digitação, não uma regra de
 * negócio (nenhuma RN-AUTH cobre isto).
 */
export function passwordsMatchValidator(passwordKey: string, confirmKey: string): ValidatorFn {
  return (group: AbstractControl): ValidationErrors | null => {
    const password = group.get(passwordKey)?.value;
    const confirm = group.get(confirmKey)?.value;
    if (confirm && password !== confirm) {
      return { passwordMismatch: true };
    }
    return null;
  };
}

/**
 * Validador de grupo: a nova senha não pode ser igual à atual (FE-12, CA-10) — checagem
 * client-side de conveniência; o backend continua sendo a autoridade final (FD-14).
 */
export function passwordsDifferentValidator(currentKey: string, newKey: string): ValidatorFn {
  return (group: AbstractControl): ValidationErrors | null => {
    const current = group.get(currentKey)?.value;
    const next = group.get(newKey)?.value;
    if (current && next && current === next) {
      return { samePassword: true };
    }
    return null;
  };
}
