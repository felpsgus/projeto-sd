import { HttpErrorResponse } from '@angular/common/http';

import { ProblemDetails } from '../api/models/problem-details';
import { AppError, AppErrorCode } from './app-error.model';
import { ERROR_MESSAGES, NOT_FOUND_MESSAGE, SERVICE_UNAVAILABLE_MESSAGE } from './error-messages';

const KNOWN_CODES: readonly AppErrorCode[] = [
  'auth.invalid_credentials',
  'auth.unauthorized',
  'network',
  'unknown',
];

function isKnownCode(code: string | undefined): code is AppErrorCode {
  return !!code && (KNOWN_CODES as readonly string[]).includes(code);
}

function isProblemDetails(body: unknown): body is ProblemDetails {
  return typeof body === 'object' && body !== null;
}

/**
 * Traduz um `HttpErrorResponse` num `AppError` (FE-03). Função pura, sem `HttpClient`
 * nem `inject()`, para ser testada isoladamente com respostas simuladas.
 *
 * **Deliberadamente "burra" (FE-03, notas técnicas):** não tenta diferenciar "e-mail não
 * existe" de "senha errada", nem "tarefa de outro usuário" de "tarefa inexistente" — o
 * backend já decidiu devolver o mesmo código/status para esses casos (RN-AUTH-09,
 * RN-AUTZ-03), e esta função só traduz o que recebeu.
 */
export function mapHttpErrorToAppError(error: HttpErrorResponse): AppError {
  if (error.status === 0) {
    return { code: 'network', message: ERROR_MESSAGES['network'], status: 0 };
  }

  const body = isProblemDetails(error.error) ? error.error : undefined;
  const traceId = body?.traceId;

  if (error.status === 400 && body?.errors) {
    return {
      code: 'unknown',
      message: 'Verifique os campos destacados.',
      status: 400,
      fieldErrors: body.errors,
      traceId,
    };
  }

  if (error.status === 404) {
    return { code: 'unknown', message: NOT_FOUND_MESSAGE, status: 404, traceId };
  }

  if (error.status === 503) {
    return { code: 'unknown', message: SERVICE_UNAVAILABLE_MESSAGE, status: 503, traceId };
  }

  const rawCode = body?.errorCode;
  if (isKnownCode(rawCode)) {
    return { code: rawCode, message: ERROR_MESSAGES[rawCode], status: error.status, traceId };
  }

  const genericMessage = `${ERROR_MESSAGES['unknown']}${traceId ? ` (ref.: ${traceId})` : ''}`;
  return { code: 'unknown', message: genericMessage, status: error.status, traceId };
}
