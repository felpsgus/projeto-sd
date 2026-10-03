/**
 * Códigos de erro emitidos pelo backend (catálogo restrito ao que o
 * frontend trata — ver FE-03). Um código novo deve ser adicionado aqui antes de
 * ser tratado em qualquer mapa de mensagens.
 */
export type ApiErrorCode =
  | 'auth.invalid_credentials'
  | 'auth.unauthorized'
  | 'auth.email_already_registered'
  | 'auth.invalid_current_password'
  | 'auth.too_many_attempts'
  | 'task.already_completed'
  | 'task.not_completed'
  | 'task.active_limit_reached';

/**
 * Corpo de erro do Gateway (`ProblemDetails` + extensão `errorCode`).
 *
 * `errors`, quando presente (400 de validação), já vem com as chaves em
 * camelCase — o Gateway normaliza (`ValidationErrorKeyNormalizer`) antes de
 * responder, então o frontend não precisa (nem deve) converter a grafia.
 */
export interface ProblemDetails {
  readonly title?: string;
  readonly status?: number;
  readonly detail?: string;
  readonly traceId?: string;
  readonly errorCode?: ApiErrorCode | string;
  readonly errors?: Readonly<Record<string, readonly string[]>>;
}
