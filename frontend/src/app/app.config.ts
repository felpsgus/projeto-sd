import {
  ApplicationConfig,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
} from '@angular/core';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { provideRouter } from '@angular/router';

import { routes } from './app.routes';
import { authInterceptor } from './core/auth/auth.interceptor';
import { clientDateInterceptor } from './core/api/client-date.interceptor';
import { errorInterceptor } from './core/errors/error.interceptor';
import { httpStatusInterceptor } from './core/http-status/http-status.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    provideRouter(routes),
    provideHttpClient(
      withFetch(),
      // Ordem importa (FE-03, notas técnicas: "auth → refresh → erro" — erro traduz por
      // último). Em Angular, o PRIMEIRO interceptor do array é o mais externo: ele processa
      // a requisição primeiro, mas a resposta/erro por ÚLTIMO (depois de todos os outros).
      // Por isso `errorInterceptor` vem primeiro aqui — para só traduzir o HttpErrorResponse
      // em AppError depois que `authInterceptor` já teve a chance de reagir a um 401 cru.
      // `clientDateInterceptor` e `httpStatusInterceptor` só leem/anexam sem transformar a
      // resposta, então sua posição relativa não afeta esse contrato.
      withInterceptors([
        errorInterceptor,
        authInterceptor,
        clientDateInterceptor,
        httpStatusInterceptor,
      ]),
    ),
  ],
};
