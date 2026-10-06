import { Injectable, signal } from '@angular/core';

import { HttpStatusSnapshot } from './http-status.model';

/**
 * Guarda só a última chamada HTTP feita à API, para o indicador discreto de status
 * (FE-03, recorte do T2) — pensado para a plateia ver 400/401/201 na demo sem abrir o
 * DevTools. Não é um histórico nem um log: um único signal, sobrescrito a cada chamada.
 */
@Injectable({ providedIn: 'root' })
export class HttpStatusService {
  private readonly lastSignal = signal<HttpStatusSnapshot | null>(null);

  readonly last = this.lastSignal.asReadonly();

  record(snapshot: HttpStatusSnapshot): void {
    this.lastSignal.set(snapshot);
  }
}
