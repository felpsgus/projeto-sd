import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { SessionStore } from './session-store';

/** Mantém quem já está autenticado fora de `/login` (FE-07). */
export const guestGuard: CanActivateFn = () => {
  const sessionStore = inject(SessionStore);
  const router = inject(Router);

  if (sessionStore.isAuthenticated()) {
    return router.createUrlTree(['/tasks']);
  }

  return true;
};
