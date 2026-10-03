import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';

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

  /** Enquanto `unknown` (bootstrap, FE-05) mostra carregamento — nunca a tela de login nem conteúdo autenticado. */
  protected readonly bootstrapping = computed(() => this.session.status() === 'unknown');
}
