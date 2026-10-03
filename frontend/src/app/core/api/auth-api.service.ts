import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ApiClient } from './api-client';
import { LoginRequest, LoginResponse } from './models/auth.models';
import { RegisterRequest, RegisterResponse } from './models/user.models';

/** Caminho absoluto (relativo à origem) do endpoint de login — usado também pelos interceptors para reconhecê-lo. */
export const LOGIN_PATH = '/api/auth/login';

/** Caminho absoluto do endpoint de cadastro (FE-08). Anônimo — nunca recebe `Authorization`. */
export const REGISTER_PATH = '/api/auth/register';

/** Renovação do access token (FE-06). Anônimo: a identidade é o cookie `HttpOnly`, nunca `Authorization`. */
export const REFRESH_PATH = '/api/auth/refresh';

/** Encerra a sessão atual (FE-10) — Bearer + cookie; o servidor apaga o cookie. */
export const LOGOUT_PATH = '/api/auth/logout';

/** Encerra todas as sessões do usuário (FE-10, RN-AUTH-19). */
export const LOGOUT_ALL_PATH = '/api/auth/logout-all';

/** Rotas de `/api/auth/*` que não levam `Authorization` nem disparam renovação (FE-06, CA-02). */
export function isAnonymousAuthPath(url: string): boolean {
  const path = url.split('?')[0] ?? url;
  return [LOGIN_PATH, REGISTER_PATH, REFRESH_PATH].some((p) => path === p || path.endsWith(p));
}

/** Serviço de recurso para autenticação: login, cadastro (FE-08), refresh (FE-06) e logout (FE-10). */
@Injectable({ providedIn: 'root' })
export class AuthApi {
  private readonly apiClient = inject(ApiClient);

  login(request: LoginRequest): Observable<LoginResponse> {
    return this.apiClient.post<LoginResponse>(LOGIN_PATH, request);
  }

  /**
   * Corpo vazio e `withCredentials`: o navegador anexa o cookie de refresh sozinho (FD-01,
   * FD-16). O servidor rotaciona o cookie; aqui só chega o novo access token.
   */
  refresh(): Observable<LoginResponse> {
    return this.apiClient.post<LoginResponse>(REFRESH_PATH, null, { withCredentials: true });
  }

  /** 204. Corpo vazio; a sessão é identificada pelo cookie e o servidor o apaga na resposta. */
  logout(): Observable<void> {
    return this.apiClient.post<void>(LOGOUT_PATH, null, { withCredentials: true });
  }

  /** 204. Revoga todas as sessões do usuário (RN-AUTH-19). */
  logoutAll(): Observable<void> {
    return this.apiClient.post<void>(LOGOUT_ALL_PATH, null, { withCredentials: true });
  }

  /** 201 em sucesso — não autentica automaticamente (o backend não emite tokens no cadastro). */
  register(request: RegisterRequest): Observable<RegisterResponse> {
    return this.apiClient.post<RegisterResponse>(REGISTER_PATH, request);
  }
}
