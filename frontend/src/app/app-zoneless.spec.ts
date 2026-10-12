import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

/**
 * FE-01, CA-07 — prova de que a reatividade por signals funciona **sem** zone.js: um
 * signal atualizado fora de qualquer evento do Angular (`setTimeout`, não um clique nem
 * uma resposta HTTP interceptada) ainda assim faz o componente re-renderizar. Se alguém
 * reintroduzir `zone.js` ou remover `provideZonelessChangeDetection()`, esse teste é o
 * primeiro a quebrar de forma óbvia.
 */
@Component({
  selector: 'app-zoneless-probe',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<p>{{ counter() }}</p>`,
})
class ZonelessProbeComponent {
  readonly counter = signal(0);

  scheduleOutsideAngular(): void {
    setTimeout(() => this.counter.set(1), 0);
  }
}

describe('reatividade zoneless (FE-01, CA-07)', () => {
  it('re-renderiza quando um signal muda dentro de um setTimeout', async () => {
    TestBed.configureTestingModule({});
    const fixture = TestBed.createComponent(ZonelessProbeComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('0');

    fixture.componentInstance.scheduleOutsideAngular();
    await new Promise((resolve) => setTimeout(resolve, 10));
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('1');
  });
});
