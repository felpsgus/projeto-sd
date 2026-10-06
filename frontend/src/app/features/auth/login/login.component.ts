import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  OnInit,
  ViewChild,
  computed,
  inject,
  signal,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { Router, ActivatedRoute, RouterLink } from '@angular/router';

import { AuthApi } from '../../../core/api/auth-api.service';
import { SessionStore } from '../../../core/auth/session-store';
import { SessionEndReason } from '../../../core/auth/session.model';
import { resolveReturnUrl } from '../../../core/auth/return-url.util';
import { AppError } from '../../../core/errors/app-error.model';
import { ERROR_MESSAGES, tooManyAttemptsMessage } from '../../../core/errors/error-messages';

/**
 * Tela de login (FE-09). O 429 do bloqueio por tentativas (RN-AUTH-13) chega já
 * traduzido pelo mapa de erros e aparece no nível do formulário. Com `Retry-After`, o
 * bloqueio é do E-MAIL (não do navegador): o botão fica desabilitado enquanto o campo
 * contém o e-mail bloqueado (normalizado) e o tempo não acabou, a mensagem é recontada por
 * minuto e some ao fim. Sem `Retry-After` não há como saber quando reabilitar: mensagem sem
 * tempo e botão habilitado. O estado é local — recarregar zera; o servidor segue em 429.
 *
 * RN-AUTH-09 é a regra mais fácil de quebrar aqui: qualquer 401 (senha errada ou e-mail
 * inexistente — o backend não distingue) mostra a **mesma** mensagem, no
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

  private readonly email = toSignal(this.form.controls.email.valueChanges, { initialValue: '' });
  private readonly block = signal<{ email: string; until: number } | null>(null);
  private readonly now = signal(Date.now());
  private timer?: ReturnType<typeof setInterval>;

  private readonly remainingSeconds = computed(() => {
    const block = this.block();
    return block ? Math.ceil((block.until - this.now()) / 1000) : 0;
  });
  protected readonly locked = computed(
    () => this.remainingSeconds() > 0 && this.block()?.email === normalize(this.email()),
  );
  /** Mensagem de bloqueio (recontada por minuto) ou o erro de formulário. */
  protected readonly message = computed(() =>
    this.locked() ? tooManyAttemptsMessage(this.remainingSeconds()) : this.formError(),
  );

  constructor() {
    inject(DestroyRef).onDestroy(() => clearInterval(this.timer));
  }

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
        return ERROR_MESSAGES['auth.unauthorized'];
      case 'session_revoked':
        return ERROR_MESSAGES['auth.refresh_token_revoked'];
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

  /** `email` é o que foi ENVIADO — o campo pode ter mudado enquanto a requisição corria. */
  private startBlock(seconds: number, email: string): void {
    const now = Date.now();
    this.now.set(now);
    this.block.set({ email: normalize(email), until: now + seconds * 1000 });
    clearInterval(this.timer);
    // Relê o relógio a cada tique (timers em aba de fundo atrasam); para ao acabar.
    this.timer = setInterval(() => {
      this.now.set(Date.now());
      if (this.remainingSeconds() <= 0) clearInterval(this.timer);
    }, 1000);
  }

  protected submit(): void {
    if (this.submitting() || this.locked() || this.form.invalid) {
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
        if (error.retryAfterSeconds) {
          this.startBlock(error.retryAfterSeconds, email);
          this.formError.set(null);
        } else {
          this.formError.set(error.message);
        }
        queueMicrotask(() => this.formErrorRef?.nativeElement.focus());
      },
    });
  }
}

/** Mesma normalização do backend: trim + minúsculas. */
function normalize(email: string): string {
  return email.trim().toLowerCase();
}
