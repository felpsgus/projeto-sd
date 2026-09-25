import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ApiClient } from './api-client';
import { LoginRequest, LoginResponse } from './models/auth.models';
import { RegisterRequest, RegisterResponse } from './models/user.models';

/** Caminho absoluto (relativo à origem) do endpoint de login — usado também pelos interceptors para reconhecê-lo. */
export const LOGIN_PATH = '/api/auth/login';

/** Caminho absoluto do endpoint de cadastro (FE-08). Anônimo — nunca recebe `Authorization`. */
export const REGISTER_PATH = '/api/auth/register';

/** Serviço de recurso para autenticação: login (recorte do T2) e cadastro (FE-08). */
@Injectable({ providedIn: 'root' })
export class AuthApi {
  private readonly apiClient = inject(ApiClient);

  login(request: LoginRequest): Observable<LoginResponse> {
    return this.apiClient.post<LoginResponse>(LOGIN_PATH, request);
  }

  /** 201 em sucesso — não autentica automaticamente (o backend não emite tokens no cadastro). */
  register(request: RegisterRequest): Observable<RegisterResponse> {
    return this.apiClient.post<RegisterResponse>(REGISTER_PATH, request);
  }
}
