import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { SessionStore } from './session-store';
import { buildSafeReturnUrl } from './return-url.util';

/**
 * Protege rotas autenticadas (FE-07). Espera o bootstrap da sessão (FE-05) antes de
 * decidir — sem isso, todo `F5` redirecionaria ao login antes de a sessão ser restaurada.
 * Sem sessão, redireciona a `/login` preservando a rota pretendida em `returnUrl` — a
 * validação de que `returnUrl` é sempre um caminho interno acontece na leitura
 * (`return-url.util.ts`), nunca aqui.
 *
 * Guard não é segurança — é navegação (FE-07, notas técnicas). A garantia real de
 * RN-AUTZ-04 está no backend: um guard driblado no cliente não dá acesso a dado nenhum.
 */
export const authGuard: CanActivateFn = async (_route, state) => {
  const sessionStore = inject(SessionStore);
  const router = inject(Router);

  await sessionStore.ready;
  if (sessionStore.isAuthenticated()) {
    return true;
  }

  return router.createUrlTree(['/login'], {
    queryParams: { returnUrl: buildSafeReturnUrl(state.url) },
  });
};
