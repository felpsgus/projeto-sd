import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ApiClient } from './api-client';
import {
  ChangePasswordRequest,
  DeleteAccountRequest,
  ProfileResponse,
  UpdateProfileRequest,
} from './models/user.models';

/** Caminho absoluto de `/api/me` — usado também para o `PATCH` e como base de `/change-password`. */
export const ME_PATH = '/api/me';
export const CHANGE_PASSWORD_PATH = '/api/me/change-password';

/** Serviço de recurso para o perfil do usuário autenticado (FE-11, FE-12, FE-13). */
@Injectable({ providedIn: 'root' })
export class UserApi {
  private readonly apiClient = inject(ApiClient);

  getMe(): Observable<ProfileResponse> {
    return this.apiClient.get<ProfileResponse>(ME_PATH);
  }

  /** Nunca envia `email` — o tipo de {@link UpdateProfileRequest} não permite (RN-USER-03). */
  updateProfile(request: UpdateProfileRequest): Observable<ProfileResponse> {
    return this.apiClient.patch<ProfileResponse>(ME_PATH, request);
  }

  /** 204 sem corpo — revoga as sessões do usuário no servidor (RN-AUTH-21/RN-AUTH-19). */
  changePassword(request: ChangePasswordRequest): Observable<void> {
    return this.apiClient.post<void>(CHANGE_PASSWORD_PATH, request);
  }

  /**
   * 204 sem corpo — exclusão imediata e irreversível (RN-USER-05). Usa `deleteWithBody`
   * porque `DELETE /api/me` exige a senha no corpo (ver nota em `ApiClient.deleteWithBody`).
   */
  deleteAccount(request: DeleteAccountRequest): Observable<void> {
    return this.apiClient.deleteWithBody<void>(ME_PATH, request);
  }
}
