import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';

import { LOGIN_PATH } from '../api/auth-api.service';
import { isApiRequest } from '../api/api-url.util';
import { SessionStore } from './session-store';

/**
 * Interceptor de autenticação (FE-06, recorte do T2).
 *
 * Anexa `Authorization: Bearer <token>` a toda requisição para a API quando há sessão
 * ativa — exceto o próprio login, que é como o token é obtido. Um **401** numa requisição
 * autenticada encerra a sessão e leva ao login com "sessão expirada" — não existe
 * renovação (proativa ou reativa) no T2, porque não há `POST /api/auth/refresh` (D-36):
 * o backend do T2 emite só um access token, então não há o que renovar.
 *
 * Um **403** não passa por aqui como caso especial: é diferença de permissão, não de
 * sessão, e o interceptor de erro (FE-03) trata a mensagem.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const sessionStore = inject(SessionStore);
  const router = inject(Router);

  const isApi = isApiRequest(req.url);
  const isLogin = req.url === LOGIN_PATH || req.url.endsWith(LOGIN_PATH);
  const token = sessionStore.accessToken();

  const request =
    isApi && !isLogin && token
      ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
      : req;

  return next(request).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === 401 && isApi && !isLogin) {
        sessionStore.endSession('session_expired');
        void router.navigateByUrl('/login');
      }
      return throwError(() => error);
    }),
  );
};
