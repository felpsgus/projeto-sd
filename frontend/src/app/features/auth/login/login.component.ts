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
import { Router, ActivatedRoute, RouterLink } from '@angular/router';

import { AuthApi } from '../../../core/api/auth-api.service';
import { SessionStore } from '../../../core/auth/session-store';
import { SessionEndReason } from '../../../core/auth/session.model';
import { resolveReturnUrl } from '../../../core/auth/return-url.util';
import { AppError } from '../../../core/errors/app-error.model';

/**
 * Tela de login (FE-09). O 429 do bloqueio por tentativas (RN-AUTH-13) chega já
 * traduzido pelo mapa de erros, com o tempo de espera de `Retry-After`, e aparece pelo
 * mesmo caminho do 401 — no nível do formulário.
 *
 * RN-AUTH-09 é a regra mais fácil de quebrar aqui: qualquer 401 (senha errada, e-mail
 * inexistente ou conta inativa — o backend não distingue) mostra a **mesma** mensagem, no
 * nível do formulário, nunca marcando um campo específico como inválido.
 */
@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule, RouterLink],
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
    // Mensagem de contexto quando o usuário chega redirecionado por um encerramento de
    // sessão (FE-06/FE-09 CA-16, FE-12 CA-02, FE-13 CA-11) — exibida antes de qualquer
    // tentativa de login. O reason é limpo depois de lido para não sobrar para a próxima visita à tela.
    const reason = this.session.lastEndReason();
    if (reason) {
      this.contextMessage.set(this.messageForEndReason(reason));
      this.session.clearLastEndReason();
    } else if (this.route.snapshot.queryParamMap.get('registered') === '1') {
      // Sucesso do cadastro (FE-08, CA-01) — não passa por `SessionStore` porque o
      // cadastro não autentica automaticamente (o backend não emite tokens nele).
      this.contextMessage.set('Conta criada com sucesso. Entre com suas credenciais.');
    }

    const email = this.route.snapshot.queryParamMap.get('email');
    if (email) {
      this.form.patchValue({ email });
    }
  }

  private messageForEndReason(reason: SessionEndReason): string {
    switch (reason) {
      case 'session_expired':
        return 'Sua sessão expirou. Entre novamente.';
      case 'session_revoked':
        return 'Sua sessão foi encerrada. Entre novamente.';
      case 'password_changed':
        // FE-12, RN-AUTH-19: a troca revoga todas as sessões; a local cai de propósito.
        return 'Sua senha foi alterada. Entre novamente com a nova senha.';
      case 'account_deleted':
        return 'Sua conta foi excluída.';
      case 'user_logout':
        // Confirmação discreta (FE-10), não um erro.
        return 'Você saiu da sua conta.';
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
