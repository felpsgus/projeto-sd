import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  Injector,
  afterNextRender,
  computed,
  signal,
  inject,
} from '@angular/core';
import {
  NavigationCancel,
  NavigationEnd,
  NavigationError,
  NavigationStart,
  RouteConfigLoadEnd,
  RouteConfigLoadStart,
  Router,
  RouterOutlet,
} from '@angular/router';

import { SessionStore } from './core/auth/session-store';
import { ROUTE_LOAD_ERROR_MESSAGE } from './core/errors/error-messages';
import { ErrorStateComponent } from './shared/ui/error-state/error-state.component';
import { LoadingComponent } from './shared/ui/loading/loading.component';
import { HttpStatusIndicatorComponent } from './shared/ui/http-status-indicator/http-status-indicator.component';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, HttpStatusIndicatorComponent, LoadingComponent, ErrorStateComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  private readonly session = inject(SessionStore);
  private readonly injector = inject(Injector);
  private readonly router = inject(Router);

  /** FE-07 CA-12: chunk de rota lazy em download. */
  protected readonly routeLoading = signal(false);
  /** FE-07 CA-13: URL cuja navegação falhou (null = sem falha). */
  protected readonly failedUrl = signal<string | null>(null);
  protected readonly routeLoadError = ROUTE_LOAD_ERROR_MESSAGE;

  protected retryNavigation(): void {
    const url = this.failedUrl();
    // ponytail: alguns navegadores guardam em cache a falha de um import() dinâmico e a nova tentativa pode falhar de novo; upgrade = location.reload().
    if (url) void this.router.navigateByUrl(url).catch(() => undefined);
  }

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
    const sub = this.router.events.subscribe((event) => {
      // RouteConfigLoadStart/End também valem para `loadComponent`; o fim e o erro/cancelamento
      // desligam o indicador para ele nunca ficar preso.
      if (event instanceof RouteConfigLoadStart) this.routeLoading.set(true);
      else if (event instanceof RouteConfigLoadEnd || event instanceof NavigationCancel)
        this.routeLoading.set(false);
      else if (event instanceof NavigationStart) this.failedUrl.set(null);
      else if (event instanceof NavigationError) {
        this.routeLoading.set(false);
        this.failedUrl.set(event.url);
      }
      if (!(event instanceof NavigationEnd)) return;
      this.routeLoading.set(false);
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
