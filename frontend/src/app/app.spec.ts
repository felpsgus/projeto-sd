import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
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

@Component({ selector: 'app-test-ok', template: '<h1>Tela ok</h1>' })
class OkComponent {}

@Component({ selector: 'app-test-lazy', template: '<h1>Tela lazy</h1>' })
class LazyComponent {}

/** FE-07 CA-12/CA-13: indicador durante o download do chunk e retry quando ele falha. */
describe('App — carregamento de rota lazy (FE-07)', () => {
  let loader: ReturnType<typeof vi.fn>;
  let pending: { resolve: () => void; reject: (e: Error) => void };

  function arm(): void {
    loader.mockImplementationOnce(
      () =>
        new Promise((resolve, reject) => {
          pending = { resolve: () => resolve(LazyComponent), reject };
        }),
    );
  }

  function setup() {
    loader = vi.fn();
    arm();
    TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideRouter([
          { path: 'lazy', loadComponent: loader as () => Promise<typeof LazyComponent> },
          { path: 'ok', component: OkComponent },
        ]),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    TestBed.inject(SessionStore).finishBootstrap();
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const router = TestBed.inject(Router);
    const el: HTMLElement = fixture.nativeElement;
    const settle = async () => {
      // sem whenStable: a navegação pendente mantém o app "instável" até o chunk resolver.
      await new Promise((r) => setTimeout(r, 0));
      fixture.detectChanges();
    };
    return { router, el, settle };
  }

  const failure = () => new Error('Failed to fetch dynamically imported module');
  const FAIL_TEXT = 'Não foi possível carregar esta página';

  it('mostra o indicador enquanto o chunk carrega e o remove ao resolver (CA-12)', async () => {
    const { router, el, settle } = setup();
    const nav = router.navigateByUrl('/lazy');
    await settle();
    expect(el.textContent).toContain('Carregando');

    pending.resolve();
    await nav;
    await settle();
    expect(el.textContent).not.toContain('Carregando');
    expect(el.textContent).toContain('Tela lazy');
  });

  it('mostra mensagem e "Tentar novamente" quando o chunk falha (CA-13)', async () => {
    const { router, el, settle } = setup();
    const nav = router.navigateByUrl('/lazy').catch(() => undefined);
    await settle();
    pending.reject(failure());
    await nav;
    await settle();

    expect(el.textContent).toContain(FAIL_TEXT);
    expect(el.textContent).toContain('Tentar novamente');
    expect(el.textContent).not.toContain('Carregando');
  });

  it('"Tentar novamente" refaz a mesma URL e, se der certo, limpa a mensagem (CA-13)', async () => {
    const { router, el, settle } = setup();
    const nav = router.navigateByUrl('/lazy').catch(() => undefined);
    await settle();
    pending.reject(failure());
    await nav;
    await settle();

    arm();
    el.querySelector<HTMLButtonElement>('button.error-state__retry')!.click();
    await settle();
    expect(loader).toHaveBeenCalledTimes(2);
    pending.resolve();
    await settle();
    await settle();

    expect(router.url).toBe('/lazy');
    expect(el.textContent).not.toContain(FAIL_TEXT);
    expect(el.textContent).toContain('Tela lazy');
  });

  it('navegar para outra rota depois da falha some com a mensagem (CA-13)', async () => {
    const { router, el, settle } = setup();
    const nav = router.navigateByUrl('/lazy').catch(() => undefined);
    await settle();
    pending.reject(failure());
    await nav;
    await settle();
    expect(el.textContent).toContain(FAIL_TEXT);

    await router.navigateByUrl('/ok');
    await settle();
    expect(el.textContent).not.toContain(FAIL_TEXT);
    expect(el.textContent).toContain('Tela ok');
  });
});
