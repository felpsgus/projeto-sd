import { Injectable, computed, signal } from '@angular/core';

import { LoginResponse } from '../api/models/auth.models';
import { SessionEndReason, SessionStatus } from './session.model';

/**
 * Única fonte de verdade sobre "quem está logado" (FE-05, escopo integral desde a Fase 4).
 *
 * O access token vive **só em memória**, num signal — nunca em `localStorage`,
 * `sessionStorage` ou cookie. O refresh token é um cookie `HttpOnly` que o frontend nunca
 * vê (FD-01): o `F5` restaura a sessão por `POST /api/auth/refresh` no bootstrap
 * (`SessionRefresher.restore`), e até ele terminar `status` fica em `'unknown'` — é o que
 * faz os guards esperarem em vez de redirecionar cedo ao login.
 *
 * O login não devolve perfil: o e-mail vem do formulário de login (ou de `GET /api/me`
 * quando a sessão é restaurada por refresh) e o `displayName` de `/api/me`.
 *
 * **`displayName` e `/account` compartilham esta única fonte (FE-11, CA-07):** a tela de
 * perfil chama `setDisplayName` ao carregar e ao salvar, e o `AppShell` só lê o signal —
 * nunca duplica o valor em outro lugar. É o desenho que evita o cabeçalho ficar defasado
 * depois de uma edição.
 */
@Injectable({ providedIn: 'root' })
export class SessionStore {
  private readonly accessTokenSignal = signal<string | null>(null);
  private readonly expiresAtSignal = signal<Date | null>(null);
  private readonly emailSignal = signal<string | null>(null);
  private readonly displayNameSignal = signal<string | null>(null);
  private readonly lastEndReasonSignal = signal<SessionEndReason | null>(null);
  private readonly bootstrappedSignal = signal(false);
  private localEndListener: ((reason: SessionEndReason) => void) | null = null;
  private resolveReady!: () => void;

  /** Resolve quando o bootstrap termina (`status` deixa de ser `'unknown'`) — os guards aguardam isto. */
  readonly ready = new Promise<void>((resolve) => (this.resolveReady = resolve));

  /** Access token atual — usado só pelo interceptor de autenticação (FE-06). Nunca logar. */
  readonly accessToken = this.accessTokenSignal.asReadonly();

  /** Quando o access token expira, conforme `expiresAt` devolvido pela API — nunca calculado localmente. */
  readonly accessTokenExpiresAt = this.expiresAtSignal.asReadonly();

  /** E-mail do usuário autenticado, para exibição no `AppShell`. */
  readonly email = this.emailSignal.asReadonly();

  /** Nome de exibição, quando já carregado (`/api/me`) — `null` até lá. */
  readonly displayName = this.displayNameSignal.asReadonly();

  /** Derivado do access token: nunca escrito manualmente (FE-05, CA-03). */
  readonly isAuthenticated = computed(() => this.accessTokenSignal() !== null);

  /** `'unknown'` durante o bootstrap; derivado, nunca escrito por fora (FE-05). */
  readonly status = computed<SessionStatus>(() => {
    if (!this.bootstrappedSignal()) {
      return 'unknown';
    }
    return this.accessTokenSignal() !== null ? 'authenticated' : 'anonymous';
  });

  /** Motivo do último encerramento, consumido pela tela de login para escolher a mensagem de contexto. */
  readonly lastEndReason = this.lastEndReasonSignal.asReadonly();

  /** Chamado após um login bem-sucedido. */
  startSession(tokens: LoginResponse, email: string): void {
    this.updateTokens(tokens);
    this.emailSignal.set(email);
    this.displayNameSignal.set(null);
  }

  /** Chamado após cada refresh bem-sucedido (e na restauração da sessão): troca só o token. */
  updateTokens(tokens: LoginResponse): void {
    this.accessTokenSignal.set(tokens.accessToken);
    this.expiresAtSignal.set(new Date(tokens.expiresAt));
    this.lastEndReasonSignal.set(null);
    this.finishBootstrap();
  }

  /** Perfil carregado por `GET /api/me` depois de restaurar a sessão. */
  setProfile(email: string, displayName: string): void {
    this.emailSignal.set(email);
    this.displayNameSignal.set(displayName);
  }

  /** Chamado por `/account` (FE-11) ao carregar o perfil e ao salvar um novo nome. */
  setDisplayName(displayName: string): void {
    this.displayNameSignal.set(displayName);
  }

  /** Bootstrap terminou: sem token restaurado, `status` vira `'anonymous'` — sem mensagem de erro. */
  finishBootstrap(): void {
    this.bootstrappedSignal.set(true);
    this.resolveReady();
  }

  /**
   * Encerra a sessão local: limpa os signals (o `TasksStore` observa `isAuthenticated` e
   * zera o estado de tarefas). Não chama o backend — quem faz isso é `LogoutService`; o
   * cookie é apagado pelo servidor. `origin: 'remote'` vem de outra aba (`SessionTabSync`)
   * e não é retransmitido.
   */
  endSession(reason: SessionEndReason, origin: 'local' | 'remote' = 'local'): void {
    this.accessTokenSignal.set(null);
    this.expiresAtSignal.set(null);
    this.emailSignal.set(null);
    this.displayNameSignal.set(null);
    this.lastEndReasonSignal.set(reason);
    this.finishBootstrap();
    if (origin === 'local') {
      this.localEndListener?.(reason);
    }
  }

  /** Registra quem quer saber de encerramentos iniciados nesta aba (sincronia entre abas); só um ouvinte. */
  onLocalEnd(listener: (reason: SessionEndReason) => void): void {
    this.localEndListener = listener;
  }

  /** Limpa a mensagem de contexto depois de exibida, para não reaparecer num próximo F5/erro. */
  clearLastEndReason(): void {
    this.lastEndReasonSignal.set(null);
  }
}
