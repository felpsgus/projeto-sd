import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router, RouterOutlet } from '@angular/router';

import { SessionStore } from '../../../core/auth/session-store';

/**
 * Layout autenticado (FE-04, recorte mínimo do T2) — cabeçalho com nome do app, e-mail do
 * usuário e "Sair"; sem menu de conta completo (sem perfil no T2). Área de conteúdo via
 * `router-outlet`.
 */
@Component({
  selector: 'app-shell',
  imports: [RouterOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="app-shell">
      <header class="app-shell__header">
        <span class="app-shell__brand">TodoList</span>
        <div class="app-shell__account">
          <span class="app-shell__email">{{ session.email() }}</span>
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

  protected logout(): void {
    // Logout local (FE-10, recorte do T2): sem POST /api/auth/logout — o backend do T2
    // não expõe esse endpoint (D-36). `replaceUrl` evita que "voltar" reexiba a tela
    // autenticada a partir do cache do roteador.
    this.session.endSession('user_logout');
    void this.router.navigateByUrl('/login', { replaceUrl: true });
  }
}
