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

  it('muda o foco para o <h1> da nova página ao trocar de rota, mas não na carga inicial (FE-21, CA-10)', async () => {
    TestBed.inject(SessionStore).finishBootstrap();
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const router = TestBed.inject(Router);

    await router.navigateByUrl('/login');
    await fixture.whenStable();
    expect(document.activeElement?.tagName).not.toBe('H1');

    await router.navigateByUrl('/register');
    await fixture.whenStable();
    expect(document.activeElement?.tagName).toBe('H1');
    expect(document.activeElement?.textContent).toContain('Criar conta');

    // Só a query string mudou: o foco (ex.: campo de busca) não é roubado.
    const input = document.createElement('input');
    document.body.appendChild(input);
    input.focus();
    await router.navigateByUrl('/register?x=1');
    await fixture.whenStable();
    expect(document.activeElement).toBe(input);
    input.remove();
  });

  it('o skip link move o foco para o conteúdo principal sem navegar (FE-21, CA-09)', async () => {
    TestBed.inject(SessionStore).finishBootstrap();
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    await TestBed.inject(Router).navigateByUrl('/login');
    await fixture.whenStable();

    const link: HTMLAnchorElement = fixture.nativeElement.querySelector('a.skip-link');
    const event = new MouseEvent('click', { bubbles: true, cancelable: true });
    link.dispatchEvent(event);

    expect(event.defaultPrevented).toBe(true);
    expect(document.activeElement?.id).toBe('main-content');
  });
});
