import { Injectable, inject } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, catchError, defaultIfEmpty, finalize, of } from 'rxjs';

import { AuthApi } from '../api/auth-api.service';
import { SessionStore } from './session-store';

/**
 * Logout voluntário (FE-10). A sessão local **sempre** encerra, mesmo com a API fora do ar
 * ou com erro — rede caída não prende o usuário logado na aba — e o erro não é exibido: do
 * ponto de vista dele, ele saiu. Sem diálogo de confirmação (reversível: basta entrar de novo).
 *
 * Se o servidor não for alcançado o cookie de refresh não é apagado (é `HttpOnly`; o
 * frontend não pode apagá-lo), mas isso não é vazamento — o próximo login o sobrescreve.
 */
@Injectable({ providedIn: 'root' })
export class LogoutService {
  private readonly authApi = inject(AuthApi);
  private readonly session = inject(SessionStore);
  private readonly router = inject(Router);

  /** `POST /api/auth/logout` — encerra só esta sessão. */
  logout(): void {
    this.run(this.authApi.logout());
  }

  /** `POST /api/auth/logout-all` — revoga as sessões de todos os dispositivos (RN-AUTH-19). */
  logoutAll(): void {
    this.run(this.authApi.logoutAll());
  }

  private run(call$: Observable<void>): void {
    call$
      .pipe(
        catchError(() => of(undefined)),
        // O interceptor de refresh cancela (EMPTY) quando a sessão já caiu: ainda assim finaliza.
        defaultIfEmpty(undefined),
        finalize(() => {
          // Sessão já encerrada pelo refresher (que também navega): nada a repetir.
          if (!this.session.isAuthenticated()) {
            return;
          }
          this.session.endSession('user_logout');
          // `replaceUrl`: "voltar" não pode reexibir a tela autenticada anterior.
          void this.router.navigateByUrl('/login', { replaceUrl: true });
        }),
      )
      .subscribe();
  }
}
