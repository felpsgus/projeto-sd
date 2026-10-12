import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';

import { HttpStatusService } from '../../../core/http-status/http-status.service';
import { environment } from '../../../../environments/environment';

/**
 * Indicador discreto do último status HTTP (FE-03, acréscimo do T2) — um canto da tela
 * mostrando, por exemplo, `POST /api/auth/login 401`, para a plateia da demo ver
 * 400/401/201 sem abrir o DevTools. Ligado por `environment.showHttpStatusIndicator`.
 */
@Component({
  selector: 'app-http-status-indicator',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (visible() && snapshot(); as snapshot) {
      <p
        class="http-status-indicator"
        [class.http-status-indicator--error]="!snapshot.ok"
        role="status"
      >
        <span class="http-status-indicator__method">{{ snapshot.method }}</span>
        <span class="http-status-indicator__url">{{ path(snapshot.url) }}</span>
        <span class="http-status-indicator__status">{{ snapshot.status }}</span>
      </p>
    }
  `,
  styleUrl: './http-status-indicator.component.scss',
})
export class HttpStatusIndicatorComponent {
  private readonly httpStatus = inject(HttpStatusService);

  protected readonly visible = computed(() => environment.showHttpStatusIndicator);
  protected readonly snapshot = this.httpStatus.last;

  protected path(url: string): string {
    try {
      return new URL(url, window.location.origin).pathname;
    } catch {
      // Justificativa: URL relativa sem base resolúvel em ambiente de teste — devolve como veio.
      return url;
    }
  }
}
