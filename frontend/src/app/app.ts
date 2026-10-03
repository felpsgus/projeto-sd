import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  Injector,
  afterNextRender,
  computed,
  inject,
} from '@angular/core';
import { NavigationEnd, Router, RouterOutlet } from '@angular/router';

import { SessionStore } from './core/auth/session-store';
import { LoadingComponent } from './shared/ui/loading/loading.component';
import { HttpStatusIndicatorComponent } from './shared/ui/http-status-indicator/http-status-indicator.component';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, HttpStatusIndicatorComponent, LoadingComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  private readonly session = inject(SessionStore);
  private readonly injector = inject(Injector);

  /** Enquanto `unknown` (bootstrap, FE-05) mostra carregamento — nunca a tela de login nem conteúdo autenticado. */
  protected readonly bootstrapping = computed(() => this.session.status() === 'unknown');

  /**
   * Com `<base href="/">` o `#main-content` do skip link resolveria para `/#main-content`
   * (navegação completa para a raiz). Intercepta e só move o foco (FE-21 CA-09).
   */
  protected skipToContent(event: Event): void {
    event.preventDefault();
    document.getElementById('main-content')?.focus();
  }

  constructor() {
    // FE-21 CA-10: mudança de rota é anunciada levando o foco ao <h1> da nova página (ou à
    // região principal, se o <h1> só aparece depois de carregar dados). A carga inicial e a
    // mudança só de query string (filtros, busca) não mexem no foco.
    let previousPath: string | null = null;
    const sub = inject(Router).events.subscribe((event) => {
      if (!(event instanceof NavigationEnd)) return;
      const path = event.urlAfterRedirects.split('?')[0];
      const changed = previousPath !== null && path !== previousPath;
      previousPath = path ?? null;
      if (!changed) return;
      afterNextRender(
        () => {
          const main = document.getElementById('main-content');
          const target = main?.querySelector<HTMLElement>('h1') ?? main;
          target?.setAttribute('tabindex', '-1');
          target?.focus();
        },
        { injector: this.injector },
      );
    });
    inject(DestroyRef).onDestroy(() => sub.unsubscribe());
  }
}
