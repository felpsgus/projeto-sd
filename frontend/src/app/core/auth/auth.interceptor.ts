import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';

import { isAnonymousAuthPath } from '../api/auth-api.service';
import { isApiRequest } from '../api/api-url.util';
import { SessionStore } from './session-store';

/**
 * Interceptor de autenticação (FE-06): anexa `Authorization: Bearer <token>` a toda
 * requisição para a API quando há sessão ativa — exceto `register`, `login` e `refresh`
 * (anônimos; o refresh se identifica só pelo cookie) e URLs fora da API, para o token
 * nunca vazar a terceiros. Sem sessão, nenhum header (nunca `Bearer null`).
 *
 * Só anexa: a renovação (proativa e reativa) e o 401 são do `refreshInterceptor`, que roda
 * **antes** deste na cadeia — assim a repetição de uma requisição passa de novo por aqui e
 * leva o token novo.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const token = inject(SessionStore).accessToken();

  if (!token || !isApiRequest(req.url) || isAnonymousAuthPath(req.url)) {
    return next(req);
  }
  return next(req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }));
};
