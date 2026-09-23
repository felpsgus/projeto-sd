/** Corpo de `POST /api/auth/login` — espelha `LoginHttpRequest` do Gateway. */
export interface LoginRequest {
  readonly email: string;
  readonly password: string;
}

/**
 * Resposta 200 de `POST /api/auth/login` — espelha `LoginHttpResponse`.
 *
 * Não existe `refreshToken`: o backend do T2 emite só um access token (D-36),
 * então não há refresh nem cookie a guardar (FD-20). Não reintroduzir esse campo.
 */
export interface LoginResponse {
  readonly accessToken: string;
  /** ISO-8601 (UTC) — quando o access token expira. */
  readonly expiresAt: string;
}
