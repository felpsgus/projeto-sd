import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { SessionStore } from './session-store';

/** Mantém quem já está autenticado fora de `/login` (FE-07). Espera o bootstrap da sessão (FE-05). */
export const guestGuard: CanActivateFn = async () => {
  const sessionStore = inject(SessionStore);
  const router = inject(Router);

  await sessionStore.ready;
  if (sessionStore.isAuthenticated()) {
    return router.createUrlTree(['/tasks']);
  }

  return true;
};
