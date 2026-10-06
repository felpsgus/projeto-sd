import { ApiErrorCode } from '../api/models/problem-details';

/** Código interno para erros que não vieram de um `ProblemDetails` do backend. */
export type AppErrorCode = ApiErrorCode | 'network' | 'unknown';

/**
 * Modelo de erro interno da aplicação (FE-03) — toda tela lida só com isto, nunca com
 * `HttpErrorResponse` ou o corpo cru do backend.
 */
export interface AppError {
  readonly code: AppErrorCode;
  readonly message: string;
  readonly status: number;
  readonly fieldErrors?: Readonly<Record<string, readonly string[]>>;
  readonly traceId?: string;
  /** Só em `auth.too_many_attempts` com `Retry-After`: segundos até o bloqueio acabar. */
  readonly retryAfterSeconds?: number;
}
