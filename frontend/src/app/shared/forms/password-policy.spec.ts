import { FormControl, FormGroup } from '@angular/forms';

import {
  evaluatePasswordRequirements,
  passwordPolicyValidator,
  passwordsDifferentValidator,
  passwordsMatchValidator,
} from './password-policy';

describe('evaluatePasswordRequirements (RN-AUTH-04)', () => {
  it('marca os três critérios como não atendidos para uma senha vazia', () => {
    expect(evaluatePasswordRequirements('')).toEqual({
      minLength: false,
      hasLetter: false,
      hasNumber: false,
    });
  });

  it('7 caracteres não atende ao mínimo de 8', () => {
    expect(evaluatePasswordRequirements('abcdef1').minLength).toBe(false);
  });

  it('só letras não atende ao critério de número', () => {
    const status = evaluatePasswordRequirements('abcdefgh');
    expect(status.hasLetter).toBe(true);
    expect(status.hasNumber).toBe(false);
  });

  it('só números não atende ao critério de letra', () => {
    const status = evaluatePasswordRequirements('12345678');
    expect(status.hasLetter).toBe(false);
    expect(status.hasNumber).toBe(true);
  });

  it('atende aos três critérios com 8+ caracteres, letra e número', () => {
    expect(evaluatePasswordRequirements('abcdef12')).toEqual({
      minLength: true,
      hasLetter: true,
      hasNumber: true,
    });
  });
});

describe('passwordPolicyValidator', () => {
  it('rejeita uma senha que não atende à política (CA-05 de FE-08)', () => {
    const control = new FormControl('abc', { nonNullable: true });
    expect(passwordPolicyValidator(control)).not.toBeNull();
  });

  it('aceita uma senha que atende aos três critérios', () => {
    const control = new FormControl('abcdef12', { nonNullable: true });
    expect(passwordPolicyValidator(control)).toBeNull();
  });
});

describe('passwordsMatchValidator', () => {
  const validator = passwordsMatchValidator('password', 'confirmPassword');

  it('rejeita quando os dois campos divergem', () => {
    const group = new FormGroup({
      password: new FormControl('abcdef12'),
      confirmPassword: new FormControl('different1'),
    });
    expect(validator(group)).toEqual({ passwordMismatch: true });
  });

  it('aceita quando os dois campos são iguais', () => {
    const group = new FormGroup({
      password: new FormControl('abcdef12'),
      confirmPassword: new FormControl('abcdef12'),
    });
    expect(validator(group)).toBeNull();
  });
});

describe('passwordsDifferentValidator (FE-12, CA-10)', () => {
  const validator = passwordsDifferentValidator('currentPassword', 'newPassword');

  it('rejeita quando a nova senha é igual à atual', () => {
    const group = new FormGroup({
      currentPassword: new FormControl('abcdef12'),
      newPassword: new FormControl('abcdef12'),
    });
    expect(validator(group)).toEqual({ samePassword: true });
  });

  it('aceita quando a nova senha é diferente da atual', () => {
    const group = new FormGroup({
      currentPassword: new FormControl('abcdef12'),
      newPassword: new FormControl('novaSenha1'),
    });
    expect(validator(group)).toBeNull();
  });
});
