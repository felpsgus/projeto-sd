import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  ViewChild,
  inject,
  signal,
} from '@angular/core';
import { FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';

import { AuthApi } from '../../../core/api/auth-api.service';
import { AppError } from '../../../core/errors/app-error.model';
import { ERROR_MESSAGES } from '../../../core/errors/error-messages';
import {
  passwordPolicyValidator,
  passwordsMatchValidator,
} from '../../../shared/forms/password-policy';
import { FormFieldErrorComponent } from '../../../shared/ui/form-field-error/form-field-error.component';
import { PasswordRequirementsComponent } from '../../../shared/ui/password-requirements/password-requirements.component';

const DISPLAY_NAME_MAX_LENGTH = 100;

type RegisterFormField = 'email' | 'password' | 'confirmPassword' | 'displayName';

/**
 * Tela de cadastro (FE-08) — `/register`, `AuthLayout`, `guestGuard`. Cria a conta via
 * `POST /api/auth/register`; **não** autentica automaticamente (o backend não emite
 * tokens nesse endpoint) — sucesso leva a `/login` com o e-mail pré-preenchido e uma
 * mensagem de confirmação.
 *
 * A validação de senha (RN-AUTH-04) é só conveniência (FD-14): o indicador ao vivo e o
 * validador vêm de `shared/forms/password-policy`, os mesmos usados por
 * `ChangePasswordComponent` (FE-12) — nunca duplicados.
 */
@Component({
  selector: 'app-register',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    FormFieldErrorComponent,
    PasswordRequirementsComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './register.component.html',
  styleUrl: './register.component.scss',
})
export class RegisterComponent {
  private readonly authApi = inject(AuthApi);
  private readonly router = inject(Router);
  private readonly formBuilder = inject(FormBuilder);

  @ViewChild('formError') private readonly formErrorRef?: ElementRef<HTMLElement>;
  @ViewChild('emailAlreadyRegistered')
  private readonly emailAlreadyRegisteredRef?: ElementRef<HTMLElement>;

  protected readonly displayNameMaxLength = DISPLAY_NAME_MAX_LENGTH;

  protected readonly submitting = signal(false);
  protected readonly formError = signal<string | null>(null);
  protected readonly serverFieldErrors = signal<Readonly<Record<string, readonly string[]>>>({});
  protected readonly emailTakenMessage = ERROR_MESSAGES['auth.email_already_registered'];
  protected readonly emailAlreadyRegistered = signal(false);

  protected readonly showPassword = signal(false);
  protected readonly showConfirmPassword = signal(false);

  protected readonly form = this.formBuilder.nonNullable.group(
    {
      email: ['', [Validators.required, Validators.email]],
      password: ['', [Validators.required, passwordPolicyValidator]],
      confirmPassword: ['', [Validators.required]],
      displayName: ['', [Validators.maxLength(DISPLAY_NAME_MAX_LENGTH)]],
    },
    { validators: [passwordsMatchValidator('password', 'confirmPassword')] },
  );

  protected togglePasswordVisibility(): void {
    this.showPassword.update((value) => !value);
  }

  protected toggleConfirmPasswordVisibility(): void {
    this.showConfirmPassword.update((value) => !value);
  }

  protected fieldMessages(field: RegisterFormField): readonly string[] {
    const control = this.form.controls[field];
    if (control.touched && control.invalid) {
      return this.clientMessages(field, control.errors);
    }
    return this.serverFieldErrors()[field] ?? [];
  }

  protected hasError(field: RegisterFormField): boolean {
    return this.fieldMessages(field).length > 0 || this.confirmPasswordMismatch();
  }

  /** Erro de grupo (senhas diferentes) — exibido junto ao campo de confirmação (CA-07). */
  protected confirmPasswordMismatch(): boolean {
    return !!this.form.errors?.['passwordMismatch'] && this.form.controls.confirmPassword.touched;
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
    this.emailAlreadyRegistered.set(false);
    this.submitting.set(true);

    const raw = this.form.getRawValue();
    const email = raw.email;
    const displayName = raw.displayName.trim();

    this.authApi
      .register({
        email,
        password: raw.password,
        displayName: displayName.length > 0 ? displayName : null,
      })
      .subscribe({
        next: () => {
          void this.router.navigate(['/login'], {
            queryParams: { registered: '1', email },
          });
        },
        error: (error: AppError) => {
          this.submitting.set(false);
          if (error.status === 409) {
            // RN-AUTH-02: diferente de login (RN-AUTH-09), o cadastro pode apontar
            // explicitamente que o e-mail já existe — o usuário precisa saber que já tem
            // conta, com um caminho direto para entrar nela.
            this.emailAlreadyRegistered.set(true);
            queueMicrotask(() => this.emailAlreadyRegisteredRef?.nativeElement.focus());
            return;
          }
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

  private clientMessages(field: RegisterFormField, errors: ValidationErrors | null): string[] {
    if (!errors) {
      return [];
    }
    if (field === 'email') {
      if (errors['required']) {
        return ['Informe seu e-mail.'];
      }
      if (errors['email']) {
        return ['Informe um e-mail válido.'];
      }
    }
    if (field === 'password') {
      if (errors['required']) {
        return ['Informe uma senha.'];
      }
      // O detalhe de qual critério falta já é mostrado pelo indicador ao vivo
      // (<app-password-requirements>) — aqui basta apontar que a senha não atende à política.
      if (errors['passwordPolicy']) {
        return ['A senha não atende aos requisitos abaixo.'];
      }
    }
    if (field === 'confirmPassword' && errors['required']) {
      return ['Confirme a senha.'];
    }
    if (field === 'displayName' && errors['maxlength']) {
      return [`O nome deve ter no máximo ${DISPLAY_NAME_MAX_LENGTH} caracteres.`];
    }
    return [];
  }

  private focusFirstInvalidField(): void {
    const order: RegisterFormField[] = ['email', 'password', 'confirmPassword', 'displayName'];
    const firstInvalid = order.find(
      (field) =>
        this.form.controls[field].invalid ||
        (this.serverFieldErrors()[field]?.length ?? 0) > 0 ||
        (field === 'confirmPassword' && this.confirmPasswordMismatch()),
    );
    if (!firstInvalid) {
      return;
    }
    queueMicrotask(() => {
      document.getElementById(firstInvalid)?.focus();
    });
  }
}
