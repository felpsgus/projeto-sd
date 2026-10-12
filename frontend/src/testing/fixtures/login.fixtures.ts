import { LoginResponse } from '../../app/core/api/models/auth.models';

/** Exemplo de payload real de `POST /api/auth/login` (200) — Gateway, `LoginHttpResponse`. */
export const LOGIN_RESPONSE_FIXTURE: LoginResponse = {
  accessToken: 'eyJhbGciOiJSUzI1NiJ9.example.token',
  expiresAt: '2026-09-22T21:15:00Z',
};
