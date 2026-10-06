import { HttpInterceptorFn } from '@angular/common/http';

import { isApiRequest } from './api-url.util';
import { CLIENT_DATE_HEADER, toLocalDateString } from './client-date.util';

/**
 * Anexa `X-Client-Date: yyyy-MM-dd` (data local do usuário) a toda requisição destinada
 * à API (FD-17). É o que permite o backend calcular `isOverdue` no fuso certo, nos
 * endpoints de tarefas, sem que cada chamada precise informar a data por conta própria.
 */
export const clientDateInterceptor: HttpInterceptorFn = (req, next) => {
  if (!isApiRequest(req.url)) {
    return next(req);
  }

  const request = req.clone({
    setHeaders: { [CLIENT_DATE_HEADER]: toLocalDateString(new Date()) },
  });

  return next(request);
};
