import { Injectable, inject } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { AuthApi } from '../api/auth-api.service';
import { UserApi } from '../api/user-api.service';
import { buildSafeReturnUrl } from './return-url.util';
import { SessionStore } from './session-store';

/** Nome do lock do Web Locks API — compartilhado por todas as abas da mesma origem. */
export const REFRESH_LOCK_NAME = 'todolist-session-refresh';

/** `refreshed`: há token novo. `ended`: o servidor recusou o refresh e a sessão local foi encerrada. */
export type RefreshOutcome = 'refreshed' | 'ended';

/**
 * Única porta de renovação do access token (FE-05 bootstrap + FE-06).
 *
 * **Dois níveis de serialização, ambos para não parecer reuso ao backend (RN-AUTH-17):**
 *
 * 1. *single-flight por aba*: N requisições que precisam de refresh ao mesmo tempo
 *    compartilham **uma** chamada (`inFlight`);
 * 2. *lock entre abas* (`navigator.locks`): o refresh token é de uso único e rotacionado, e
 *    o cookie é compartilhado pelas abas. Dois refreshes simultâneos com o mesmo cookie
 *    derrubariam a sessão inteira. Com o lock, a segunda aba espera a primeira terminar e
 *    só então faz o seu refresh — já com o cookie rotacionado. Sem Web Locks (navegador
 *    antigo, jsdom), cai no single-flight por aba apenas.
 *
 * Erro transitório (503, rede) **não** é sessão inválida: a promessa rejeita com o erro e a
 * sessão continua como estava. Só o 401 do refresh encerra a sessão.
 */
@Injectable({ providedIn: 'root' })
export class SessionRefresher {
  private readonly authApi = inject(AuthApi);
  private readonly userApi = inject(UserApi);
  private readonly session = inject(SessionStore);
  private readonly router = inject(Router);

  private inFlight: Promise<RefreshOutcome> | null = null;

  /** Renova o access token. Chamadas concorrentes recebem a mesma promessa. */
  refresh(): Promise<RefreshOutcome> {
    if (!this.inFlight) {
      const attempt = this.runWithLock().finally(() => (this.inFlight = null));
      this.inFlight = attempt;
    }
    return this.inFlight;
  }

  /**
   * Bootstrap (FE-05): tenta restaurar a sessão pelo cookie antes de as rotas ativarem.
   * Qualquer falha termina em `anonymous` **sem mensagem** — quem chega sem sessão não fez
   * nada de errado. Depois do refresh, `GET /api/me` popula e-mail e nome de exibição.
   */
  async restore(): Promise<void> {
    try {
      if ((await this.refresh()) === 'refreshed') {
        const profile = await firstValueFrom(this.userApi.getMe());
        this.session.setProfile(profile.email, profile.displayName);
      }
    } catch {
      // Servidor fora ou /api/me falhou: o usuário cai no login (anônimo) ou segue sem o
      // nome de exibição (o shell cai no e-mail) — nenhum dos dois é motivo de alarme.
    } finally {
      this.session.finishBootstrap();
    }
  }

  private runWithLock(): Promise<RefreshOutcome> {
    const run = () => this.callRefresh();
    return typeof navigator !== 'undefined' && navigator.locks
      ? navigator.locks.request(REFRESH_LOCK_NAME, run)
      : run();
  }

  private async callRefresh(): Promise<RefreshOutcome> {
    try {
      this.session.updateTokens(await firstValueFrom(this.authApi.refresh()));
      return 'refreshed';
    } catch (error) {
      const { status, code } = error as { status?: number; code?: string };
      if (status !== 401) {
        throw error;
      }
      this.invalidateSession(
        code === 'auth.refresh_token_revoked' ? 'session_revoked' : 'session_expired',
      );
      return 'ended';
    }
  }

  /** O refresh foi recusado: revogado por ação do usuário (RN-AUTH-19) ou expirado/reusado/inválido. */
  private invalidateSession(reason: 'session_expired' | 'session_revoked'): void {
    if (this.session.status() === 'unknown') {
      // Bootstrap: não havia sessão a perder. `restore` encerra o bootstrap como `anonymous`, sem mensagem.
      return;
    }
    this.session.endSession(reason);
    const url = this.router.url;
    const onAuthPage = url.startsWith('/login') || url.startsWith('/register');
    void this.router.navigate(['/login'], {
      queryParams: onAuthPage ? {} : { returnUrl: buildSafeReturnUrl(url) },
      replaceUrl: true,
    });
  }
}
