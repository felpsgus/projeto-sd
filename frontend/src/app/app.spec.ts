import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { App } from './app';
import { SessionStore } from './core/auth/session-store';
import { routes } from './app.routes';

/** Smoke test (FE-01): a aplicação inicializa e a rota raiz renderiza sem erro. */
describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter(routes), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('renderiza o skip link como primeiro elemento focável (FE-04, CA-07)', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    await fixture.whenStable();

    const skipLink = fixture.nativeElement.querySelector('a.skip-link');
    expect(skipLink?.getAttribute('href')).toBe('#main-content');
  });

  it('mostra carregamento enquanto o bootstrap da sessão não termina (FE-05, CA-07)', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('Carregando');

    TestBed.inject(SessionStore).finishBootstrap();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).not.toContain('Carregando');
  });

  it('a rota raiz redireciona (visitante cai no login por não ter sessão)', async () => {
    TestBed.inject(SessionStore).finishBootstrap();
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    const router = TestBed.inject(Router);
    await router.navigateByUrl('/');
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('Entrar');
  });
});
