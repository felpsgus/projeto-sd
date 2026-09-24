import { CanDeactivateFn } from '@angular/router';

/** Implementado por qualquer container com um formulário que pode ficar "sujo" (FE-18, CA-23). */
export interface CanComponentDeactivate {
  canDeactivate(): boolean;
}

/**
 * Guarda genérica de saída (FE-18) — delega a decisão ao próprio componente, que sabe se
 * o formulário tem alterações não salvas. Hoje usada só por `EditTaskComponent`, mas não
 * depende dele: qualquer container que implemente `CanComponentDeactivate` pode reutilizá-la.
 */
export const unsavedChangesGuard: CanDeactivateFn<CanComponentDeactivate> = (component) =>
  component.canDeactivate();
