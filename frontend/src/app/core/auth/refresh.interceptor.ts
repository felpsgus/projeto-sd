import { HttpErrorResponse, HttpEvent, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { EMPTY, Observable, catchError, from, of, switchMap, throwError } from 'rxjs';

import { environment } from '../../../environments/environment';
import { isAnonymousAuthPath } from '../api/auth-api.service';
import { isApiRequest } from '../api/api-url.util';
import { RefreshOutcome, SessionRefresher } from './session-refresher';
import { SessionStore } from './session-store';

/**
 * Renovação transparente do access token (FE-06, FD-13). Fica **fora** do
 * `authInterceptor` (que só anexa o Bearer) e **dentro** do `errorInterceptor`, que só
 * traduz o erro depois — ver a ordem em `app.config.ts`.
 *
 * - **Proativa:** se o token expira em menos de `refreshSkewSeconds`, renova antes de
 *   enviar. Usa o `expiresAt` da API, nunca o `exp` do JWT. Se a renovação proativa falhar
 *   por erro transitório, a requisição segue com o token atual (a reativa ainda pode salvá-la).
 * - **Reativa:** um 401 renova e repete a requisição **uma única vez**; um segundo 401 na
 *   repetição vira erro normal (sem laço). Se o token já foi trocado enquanto a resposta
 *   estava em voo (outra requisição renovou), repete sem renovar de novo.
 * - **Single-flight:** vive em `SessionRefresher`; N requisições → uma chamada de refresh.
 * - Refresh recusado: a sessão já foi encerrada pelo refresher; a requisição é **cancelada**
 *   (`EMPTY`), sem erro para a tela exibir depois do redirecionamento (CA-15).
 * - 403, 404, 409 e status 0 nunca chegam ao refresh — só o 401.
 */
export const refreshInterceptor: HttpInterceptorFn = (req, next) => {
  const session = inject(SessionStore);
  const refresher = inject(SessionRefresher);

  if (!isApiRequest(req.url) || isAnonymousAuthPath(req.url)) {
    return next(req);
  }

  const expiresAt = session.accessTokenExpiresAt();
  const expiresSoon =
    expiresAt !== null && expiresAt.getTime() - Date.now() < environment.refreshSkewSeconds * 1000;

  const ready$: Observable<RefreshOutcome | 'skipped' | null> = expiresSoon
    ? from(refresher.refresh().catch(() => 'skipped' as const))
    : of(null);

  return ready$.pipe(
    switchMap((outcome): Observable<HttpEvent<unknown>> => {
      if (outcome === 'ended') {
        return EMPTY;
      }
      const tokenSent = session.accessToken();
      return next(req).pipe(
        catchError((error: unknown) => {
          if (!(error instanceof HttpErrorResponse) || error.status !== 401 || !tokenSent) {
            return throwError(() => error);
          }
          // Outra requisição já renovou enquanto esta estava em voo: só repete.
          if (session.accessToken() !== tokenSent) {
            return session.isAuthenticated() ? next(req) : EMPTY;
          }
          return from(refresher.refresh()).pipe(
            switchMap((result) => (result === 'ended' ? EMPTY : next(req))),
          );
        }),
      );
    }),
  );
};
