import { HttpErrorResponse, HttpEventType, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { tap } from 'rxjs';

import { isApiRequest } from '../api/api-url.util';
import { HttpStatusService } from './http-status.service';

/**
 * Alimenta o indicador discreto de status (FE-03, recorte T2) com método, rota e status
 * de toda chamada à API — sucesso ou erro. Só observa a resposta; nunca a transforma, e
 * por isso é inofensivo estar em qualquer posição da cadeia de interceptors.
 */
export const httpStatusInterceptor: HttpInterceptorFn = (req, next) => {
  const httpStatus = inject(HttpStatusService);

  if (!isApiRequest(req.url)) {
    return next(req);
  }

  return next(req).pipe(
    tap({
      next: (event) => {
        if (event.type === HttpEventType.Response) {
          httpStatus.record({
            method: req.method,
            url: req.urlWithParams,
            status: event.status,
            ok: true,
            at: new Date(),
          });
        }
      },
      error: (error: unknown) => {
        const status = error instanceof HttpErrorResponse ? error.status : 0;
        httpStatus.record({
          method: req.method,
          url: req.urlWithParams,
          status,
          ok: false,
          at: new Date(),
        });
      },
    }),
  );
};
