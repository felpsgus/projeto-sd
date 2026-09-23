import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { App } from './app';
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

  it('a rota raiz redireciona (visitante cai no login por não ter sessão)', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    const router = TestBed.inject(Router);
    await router.navigateByUrl('/');
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('Entrar');
  });
});
