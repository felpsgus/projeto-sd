import { Injectable, computed, signal } from '@angular/core';

import { LoginResponse } from '../api/models/auth.models';
import { SessionEndReason } from './session.model';

/**
 * Única fonte de verdade sobre "quem está logado" (FE-05, recorte do T2).
 *
 * O access token vive **só em memória**, num signal (FD-20) — nunca em `localStorage`,
 * `sessionStorage` ou cookie. Recarregar a página (`F5`) derruba a sessão: não há bootstrap
 * por refresh no T2 porque não há refresh token nenhum a partir do qual restaurá-la (o
 * backend do T2 emite só um access token — D-36). Isso é esperado e aceito pelo recorte,
 * não um bug a corrigir aqui.
 *
 * O login não devolve perfil — o e-mail exibido no `AppShell` vem do formulário de login.
 * O nome de exibição (`displayName`) só fica conhecido quando `/account` (FE-11) carrega
 * `GET /api/me`; até lá, o cabeçalho cai no e-mail (ver `AppShellComponent`).
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

  /** Access token atual — usado só pelo interceptor de autenticação (FE-06). Nunca logar. */
  readonly accessToken = this.accessTokenSignal.asReadonly();

  /** Quando o access token expira, conforme `expiresAt` devolvido pela API — nunca calculado localmente. */
  readonly accessTokenExpiresAt = this.expiresAtSignal.asReadonly();

  /** E-mail do usuário autenticado, para exibição no `AppShell`. */
  readonly email = this.emailSignal.asReadonly();

  /** Nome de exibição, quando já carregado por `/account` (FE-11) — `null` até lá. */
  readonly displayName = this.displayNameSignal.asReadonly();

  /** Derivado do access token: nunca escrito manualmente (FE-05, CA-03). */
  readonly isAuthenticated = computed(() => this.accessTokenSignal() !== null);

  /** Motivo do último encerramento, consumido pela tela de login para escolher a mensagem de contexto. */
  readonly lastEndReason = this.lastEndReasonSignal.asReadonly();

  /** Chamado após um login bem-sucedido. */
  startSession(tokens: LoginResponse, email: string): void {
    this.accessTokenSignal.set(tokens.accessToken);
    this.expiresAtSignal.set(new Date(tokens.expiresAt));
    this.emailSignal.set(email);
    this.displayNameSignal.set(null);
    this.lastEndReasonSignal.set(null);
  }

  /** Chamado por `/account` (FE-11) ao carregar o perfil e ao salvar um novo nome. */
  setDisplayName(displayName: string): void {
    this.displayNameSignal.set(displayName);
  }

  /**
   * Encerra a sessão local. Não existe chamada ao backend aqui — no T2 não há
   * `POST /api/auth/logout` (D-36) nem cookie a apagar; quem faz a chamada de rede,
   * quando existir, é a feature que decidiu encerrar (FE-10).
   */
  endSession(reason: SessionEndReason): void {
    this.accessTokenSignal.set(null);
    this.expiresAtSignal.set(null);
    this.emailSignal.set(null);
    this.displayNameSignal.set(null);
    this.lastEndReasonSignal.set(reason);
  }

  /** Limpa a mensagem de contexto depois de exibida, para não reaparecer num próximo F5/erro. */
  clearLastEndReason(): void {
    this.lastEndReasonSignal.set(null);
  }
}
