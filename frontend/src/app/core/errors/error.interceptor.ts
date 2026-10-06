import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { catchError, throwError } from 'rxjs';

import { mapHttpErrorToAppError } from './error-mapper';

/**
 * Último elo da cadeia de interceptors (FE-03): traduz qualquer erro HTTP restante num
 * `AppError` e o relança — nenhuma tela lida com `HttpErrorResponse` diretamente.
 *
 * Roda depois do interceptor de autenticação (FE-06): o 401 de sessão já foi tratado lá
 * (encerra a sessão e navega ao login); aqui ele só vira uma mensagem, sem repetir a
 * navegação.
 */
export const errorInterceptor: HttpInterceptorFn = (req, next) =>
  next(req).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse) {
        return throwError(() => mapHttpErrorToAppError(error));
      }
      return throwError(() => error);
    }),
  );
