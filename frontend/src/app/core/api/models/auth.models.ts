/** Corpo de `POST /api/auth/login` — espelha `LoginHttpRequest` do Gateway. */
export interface LoginRequest {
  readonly email: string;
  readonly password: string;
}

/**
 * Resposta 200 de `POST /api/auth/login` e `POST /api/auth/refresh` — espelha
 * `LoginHttpResponse`.
 *
 * Não existe campo de refresh token aqui, de propósito: ele vai e volta num cookie
 * `HttpOnly` que o JavaScript nunca vê (FD-01, RN-AUTH-20). Não reintroduzir esse campo.
 */
export interface LoginResponse {
  readonly accessToken: string;
  /** ISO-8601 (UTC) — quando o access token expira. */
  readonly expiresAt: string;
}
