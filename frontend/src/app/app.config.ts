import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
} from '@angular/core';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { provideRouter } from '@angular/router';

import { routes } from './app.routes';
import { authInterceptor } from './core/auth/auth.interceptor';
import { refreshInterceptor } from './core/auth/refresh.interceptor';
import { SessionRefresher } from './core/auth/session-refresher';
import { SessionTabSync } from './core/auth/session-tab-sync';
import { clientDateInterceptor } from './core/api/client-date.interceptor';
import { errorInterceptor } from './core/errors/error.interceptor';
import { httpStatusInterceptor } from './core/http-status/http-status.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    provideRouter(routes),
    // Bootstrap da sessão (FE-05): dispara a restauração por refresh e a sincronia entre
    // abas, mas NÃO bloqueia a inicialização — o `App` mostra "carregando" enquanto o
    // status é `unknown` e os guards aguardam `SessionStore.ready`.
    provideAppInitializer(() => {
      inject(SessionTabSync).start();
      void inject(SessionRefresher).restore();
    }),
    provideHttpClient(
      withFetch(),
      // Ordem importa (FE-03/FE-06: auth → refresh → erro; o erro traduz por último). Em
      // Angular, o PRIMEIRO interceptor do array é o mais externo: processa a requisição
      // primeiro, mas a resposta/erro por ÚLTIMO. Por isso:
      // - `errorInterceptor` primeiro: só traduz o HttpErrorResponse em AppError depois que
      //   o refresh já teve a chance de reagir a um 401 cru;
      // - `refreshInterceptor` antes de `authInterceptor`: a repetição após o refresh passa
      //   de novo pelo `authInterceptor`, que anexa o Bearer novo.
      // `clientDateInterceptor` e `httpStatusInterceptor` só leem/anexam sem transformar a
      // resposta, então sua posição relativa não afeta esse contrato.
      withInterceptors([
        errorInterceptor,
        refreshInterceptor,
        authInterceptor,
        clientDateInterceptor,
        httpStatusInterceptor,
      ]),
    ),
  ],
};
