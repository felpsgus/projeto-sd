import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ApiClient } from './api-client';
import { LoginRequest, LoginResponse } from './models/auth.models';

/** Caminho absoluto (relativo à origem) do endpoint de login — usado também pelos interceptors para reconhecê-lo. */
export const LOGIN_PATH = '/api/auth/login';

/** Serviço de recurso para autenticação (recorte do T2: só login). */
@Injectable({ providedIn: 'root' })
export class AuthApi {
  private readonly apiClient = inject(ApiClient);

  login(request: LoginRequest): Observable<LoginResponse> {
    return this.apiClient.post<LoginResponse>(LOGIN_PATH, request);
  }
}
