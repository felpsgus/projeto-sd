/** Corpo de `POST /api/auth/register` — espelha `RegisterHttpRequest` do Gateway. */
export interface RegisterRequest {
  readonly email: string;
  readonly password: string;
  /** `null`/omitido: o backend usa a parte do e-mail antes do `@` (RN-AUTH-07). */
  readonly displayName?: string | null;
}

/** Espelha `ProfileHttpResponse` do Gateway — resposta de `GET /api/me`, `PATCH /api/me` e `POST /api/auth/register`. */
export interface ProfileResponse {
  readonly id: string;
  readonly email: string;
  readonly displayName: string;
  /** ISO-8601 (UTC). */
  readonly createdAt: string;
}

/** `POST /api/auth/register` responde 201 com o mesmo shape de {@link ProfileResponse}. */
export type RegisterResponse = ProfileResponse;

/**
 * Corpo de `PATCH /api/me` — só o nome de exibição (RN-USER-02). O e-mail **nunca** é
 * enviado aqui, mesmo que o backend o ignore (RN-USER-03/CA-09 de FE-14 no backend): o tipo
 * não tem o campo, então não há como um chamador incluí-lo por engano.
 */
export interface UpdateProfileRequest {
  readonly displayName: string;
}

/** Corpo de `POST /api/me/change-password` (RN-AUTH-21). Sucesso é 204, sem corpo de resposta. */
export interface ChangePasswordRequest {
  readonly currentPassword: string;
  readonly newPassword: string;
}

/** Corpo de `DELETE /api/me` (D-19 do backend) — a senha confirma a exclusão. Sucesso é 204. */
export interface DeleteAccountRequest {
  readonly password: string;
}
