import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  ViewChild,
  inject,
  signal,
} from '@angular/core';
import { FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { Router } from '@angular/router';

import { UserApi } from '../../../core/api/user-api.service';
import { SessionStore } from '../../../core/auth/session-store';
import { AppError } from '../../../core/errors/app-error.model';
import {
  passwordPolicyValidator,
  passwordsDifferentValidator,
  passwordsMatchValidator,
} from '../../../shared/forms/password-policy';
import { FormFieldErrorComponent } from '../../../shared/ui/form-field-error/form-field-error.component';
import { PasswordRequirementsComponent } from '../../../shared/ui/password-requirements/password-requirements.component';

type ChangePasswordField = 'currentPassword' | 'newPassword' | 'confirmNewPassword';

/**
 * Troca de senha (FE-12) — `/account/password`, `AppShell`, `authGuard`.
 *
 * **RN-AUTH-19 (Fase 4, 03/10/2026):** o backend revoga **todas** as sessões do usuário,
 * inclusive a atual — o próximo refresh falharia. Por isso o sucesso encerra a sessão local
 * na hora (motivo `password_changed`) e leva ao login com "senha alterada, entre novamente",
 * em vez de deixar a aba numa sessão que vai morrer. O texto do aviso prévio (CA-06) segue
 * como está: continua verdadeiro e não promete mais do que a tela mostra.
 *
 * O indicador de requisitos de senha é o mesmo de `RegisterComponent` (FE-08) —
 * `<app-password-requirements>` e os validadores de `shared/forms/password-policy`, nunca
 * duplicados (FE-12, notas técnicas).
 */
@Component({
  selector: 'app-change-password',
  imports: [ReactiveFormsModule, FormFieldErrorComponent, PasswordRequirementsComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './change-password.component.html',
  styleUrl: './change-password.component.scss',
})
export class ChangePasswordComponent {
  private readonly userApi = inject(UserApi);
  private readonly session = inject(SessionStore);
  private readonly router = inject(Router);
  private readonly formBuilder = inject(FormBuilder);

  @ViewChild('formError') private readonly formErrorRef?: ElementRef<HTMLElement>;

  protected readonly submitting = signal(false);
  protected readonly formError = signal<string | null>(null);
  protected readonly serverFieldErrors = signal<Readonly<Record<string, readonly string[]>>>({});

  protected readonly showCurrentPassword = signal(false);
  protected readonly showNewPassword = signal(false);

  protected readonly form = this.formBuilder.nonNullable.group(
    {
      currentPassword: ['', [Validators.required]],
      newPassword: ['', [Validators.required, passwordPolicyValidator]],
      confirmNewPassword: ['', [Validators.required]],
    },
    {
      validators: [
        passwordsMatchValidator('newPassword', 'confirmNewPassword'),
        passwordsDifferentValidator('currentPassword', 'newPassword'),
      ],
    },
  );

  protected toggleCurrentPasswordVisibility(): void {
    this.showCurrentPassword.update((value) => !value);
  }

  protected toggleNewPasswordVisibility(): void {
    this.showNewPassword.update((value) => !value);
  }

  protected fieldMessages(field: ChangePasswordField): readonly string[] {
    const control = this.form.controls[field];
    if (control.touched && control.invalid) {
      return this.clientMessages(field, control.errors);
    }
    const groupMessages = this.groupMessages(field);
    if (groupMessages.length > 0) {
      return groupMessages;
    }
    return this.serverFieldErrors()[field] ?? [];
  }

  protected hasError(field: ChangePasswordField): boolean {
    return this.fieldMessages(field).length > 0;
  }

  protected submit(): void {
    if (this.submitting()) {
      return;
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.focusFirstInvalidField();
      return;
    }

    this.formError.set(null);
    this.serverFieldErrors.set({});
    this.submitting.set(true);

    const { currentPassword, newPassword } = this.form.getRawValue();
    // Capturado antes de `endSession` — que limpa o e-mail — para pré-preencher o login (CA-05).
    const email = this.session.email();

    this.userApi.changePassword({ currentPassword, newPassword }).subscribe({
      next: () => {
        // Encerra a sessão local imediatamente (CA-03): o refresh token já foi revogado no
        // servidor (RN-AUTH-19); o access token ainda vale por alguns minutos, mas manter a
        // aba "funcionando" até o próximo refresh falhar criaria um estado ambíguo (FE-12).
        this.session.endSession('password_changed');
        void this.router.navigate(['/login'], {
          queryParams: email ? { email } : {},
          replaceUrl: true,
        });
      },
      error: (error: AppError) => {
        this.submitting.set(false);
        // Em qualquer ramo de erro a sessão permanece ativa — só o sucesso (204) encerra
        // (CA-12/CA-15). Senha atual incorreta chega como 400 com `errors.currentPassword`
        // (não 401): o interceptor de autenticação não trata 400 como sessão expirada, então
        // não há risco de deslogar por engano aqui.
        if (error.status === 400 && error.fieldErrors) {
          this.serverFieldErrors.set(error.fieldErrors);
          this.focusFirstInvalidField();
          return;
        }
        this.formError.set(error.message);
        queueMicrotask(() => this.formErrorRef?.nativeElement.focus());
      },
    });
  }

  private groupMessages(field: ChangePasswordField): string[] {
    if (
      field === 'confirmNewPassword' &&
      this.form.errors?.['passwordMismatch'] &&
      this.form.controls.confirmNewPassword.touched
    ) {
      return ['As senhas não coincidem.'];
    }
    if (
      field === 'newPassword' &&
      this.form.errors?.['samePassword'] &&
      this.form.controls.newPassword.touched
    ) {
      return ['A nova senha deve ser diferente da atual.'];
    }
    return [];
  }

  private clientMessages(field: ChangePasswordField, errors: ValidationErrors | null): string[] {
    if (!errors) {
      return [];
    }
    if (field === 'currentPassword' && errors['required']) {
      return ['Informe sua senha atual.'];
    }
    if (field === 'newPassword') {
      if (errors['required']) {
        return ['Informe a nova senha.'];
      }
      if (errors['passwordPolicy']) {
        return ['A nova senha não atende aos requisitos abaixo.'];
      }
    }
    if (field === 'confirmNewPassword' && errors['required']) {
      return ['Confirme a nova senha.'];
    }
    return [];
  }

  private focusFirstInvalidField(): void {
    const order: ChangePasswordField[] = ['currentPassword', 'newPassword', 'confirmNewPassword'];
    const firstInvalid = order.find((field) => this.hasError(field));
    if (!firstInvalid) {
      return;
    }
    queueMicrotask(() => {
      document.getElementById(firstInvalid)?.focus();
    });
  }
}
