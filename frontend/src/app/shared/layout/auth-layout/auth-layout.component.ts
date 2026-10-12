import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';

/**
 * Layout público (FE-04, recorte mínimo do T2) — usado só por `/login` no recorte
 * (sem cadastro, fora do T2). Cartão centralizado, sem navegação.
 */
@Component({
  selector: 'app-auth-layout',
  imports: [RouterOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <main id="main-content" class="auth-layout" tabindex="-1">
      <div class="auth-layout__card">
        <router-outlet />
      </div>
    </main>
  `,
  styleUrl: './auth-layout.component.scss',
})
export class AuthLayoutComponent {}
