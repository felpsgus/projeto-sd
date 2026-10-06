import { DestroyRef, Injectable, inject } from '@angular/core';
import { Router } from '@angular/router';

import { SessionStore } from './session-store';
import { SessionEndReason } from './session.model';

const CHANNEL_NAME = 'todolist-session';

/**
 * Sincronia de encerramento entre abas (FE-05, CA-15): sair, trocar a senha ou ter a sessão
 * recusada numa aba encerra a sessão nas demais. Usa `BroadcastChannel`, que só carrega o
 * **motivo** — nenhum token, nenhum dado de usuário — e é nativo, sem storage. Sem suporte,
 * cada aba segue só com o seu estado.
 */
@Injectable({ providedIn: 'root' })
export class SessionTabSync {
  private readonly session = inject(SessionStore);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  start(): void {
    if (typeof BroadcastChannel === 'undefined') {
      return;
    }
    const channel = new BroadcastChannel(CHANNEL_NAME);
    this.destroyRef.onDestroy(() => channel.close());

    this.session.onLocalEnd((reason) => channel.postMessage(reason));
    channel.onmessage = (event: MessageEvent<SessionEndReason>) => {
      if (!this.session.isAuthenticated()) {
        return;
      }
      this.session.endSession(event.data, 'remote');
      void this.router.navigateByUrl('/login', { replaceUrl: true });
    };
  }
}
