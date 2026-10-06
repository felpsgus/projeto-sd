import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';

/** Página 404 (FE-01/FE-07, CA-14) — rota curinga (`**`), com link para voltar. */
@Component({
  selector: 'app-not-found',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <main id="main-content" class="not-found" tabindex="-1">
      <h1>Página não encontrada</h1>
      <p>O endereço acessado não existe.</p>
      <a routerLink="/">Voltar ao início</a>
    </main>
  `,
  styles: `
    .not-found {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      min-height: 100vh;
      gap: var(--space-sm);
      padding: var(--space-md);
      text-align: center;
      color: var(--color-text);
      background: var(--color-bg);
    }

    .not-found p {
      color: var(--color-text-secondary);
    }

    .not-found a {
      margin-top: var(--space-xs);
    }
  `,
})
export class NotFoundComponent {}
