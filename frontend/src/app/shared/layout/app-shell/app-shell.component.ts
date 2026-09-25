import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { Router, RouterLink, RouterOutlet } from '@angular/router';

import { SessionStore } from '../../../core/auth/session-store';

/**
 * Layout autenticado (FE-04) — cabeçalho com nome do app, identificação do usuário, link
 * para "Minha conta" (FE-11) e "Sair". Área de conteúdo via `router-outlet`.
 */
@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="app-shell">
      <header class="app-shell__header">
        <span class="app-shell__brand">TodoList</span>
        <div class="app-shell__account">
          <a routerLink="/account" class="app-shell__name">{{ displayName() }}</a>
          <button type="button" class="app-shell__logout" (click)="logout()">Sair</button>
        </div>
      </header>
      <main id="main-content" class="app-shell__content" tabindex="-1">
        <router-outlet />
      </main>
    </div>
  `,
  styleUrl: './app-shell.component.scss',
})
export class AppShellComponent {
  protected readonly session = inject(SessionStore);
  private readonly router = inject(Router);

  /**
   * Nome de exibição quando já conhecido (perfil carregado por `/account`, FE-11), com
   * fallback para o e-mail (FE-11, CA-07) — a mesma fonte (`SessionStore`) em qualquer um
   * dos dois casos, nunca um segundo estado duplicado aqui.
   */
  protected readonly displayName = computed(
    () => this.session.displayName() ?? this.session.email(),
  );

  protected logout(): void {
    // Logout local (FE-10, recorte do T2): sem POST /api/auth/logout — o backend do T2
    // não expõe esse endpoint (D-36). `replaceUrl` evita que "voltar" reexiba a tela
    // autenticada a partir do cache do roteador.
    this.session.endSession('user_logout');
    void this.router.navigateByUrl('/login', { replaceUrl: true });
  }
}
