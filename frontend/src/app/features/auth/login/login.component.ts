import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  OnInit,
  ViewChild,
  inject,
  signal,
} from '@angular/core';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';

import { AuthApi } from '../../../core/api/auth-api.service';
import { SessionStore } from '../../../core/auth/session-store';
import { resolveReturnUrl } from '../../../core/auth/return-url.util';
import { AppError } from '../../../core/errors/app-error.model';

/**
 * Tela de login (FE-09, recorte do T2 — sem bloqueio por tentativas/429, que depende de
 * BE-12, fora do T2).
 *
 * RN-AUTH-09 é a regra mais fácil de quebrar aqui: qualquer 401 (senha errada, e-mail
 * inexistente ou conta inativa — o backend não distingue) mostra a **mesma** mensagem, no
 * nível do formulário, nunca marcando um campo específico como inválido.
 */
@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './login.component.html',
  styleUrl: './login.component.scss',
})
export class LoginComponent implements OnInit {
  private readonly authApi = inject(AuthApi);
  private readonly session = inject(SessionStore);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly formBuilder = inject(FormBuilder);

  @ViewChild('formError') private readonly formErrorRef?: ElementRef<HTMLElement>;

  protected readonly submitting = signal(false);
  protected readonly formError = signal<string | null>(null);
  protected readonly contextMessage = signal<string | null>(null);

  protected readonly form = this.formBuilder.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required]],
  });

  ngOnInit(): void {
    // Mensagem de contexto quando o usuário chega redirecionado por um 401 de sessão
    // (FE-06/FE-09, CA-16) — exibida antes de qualquer tentativa de login.
    if (this.session.lastEndReason() === 'session_expired') {
      this.contextMessage.set('Sua sessão expirou. Entre novamente.');
      this.session.clearLastEndReason();
    }
  }

  protected submit(): void {
    if (this.submitting() || this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.formError.set(null);
    this.submitting.set(true);
    const { email, password } = this.form.getRawValue();

    this.authApi.login({ email, password }).subscribe({
      next: (response) => {
        this.session.startSession(response, email);
        const returnUrl = resolveReturnUrl(this.route.snapshot.queryParamMap.get('returnUrl'));
        void this.router.navigateByUrl(returnUrl);
      },
      error: (error: AppError) => {
        this.submitting.set(false);
        // Mensagem única (RN-AUTH-09): o erro é do formulário, nunca de um campo — nem
        // sequer distinguimos por código aqui além do que o mapa central já traduziu.
        this.formError.set(error.message);
        queueMicrotask(() => this.formErrorRef?.nativeElement.focus());
      },
    });
  }
}
