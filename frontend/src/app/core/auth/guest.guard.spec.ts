import { TestBed } from '@angular/core/testing';
import { UrlTree, provideRouter } from '@angular/router';
import { ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';

import { guestGuard } from './guest.guard';
import { SessionStore } from './session-store';

function runGuard() {
  const route = {} as ActivatedRouteSnapshot;
  const state = {} as RouterStateSnapshot;
  return TestBed.runInInjectionContext(() => guestGuard(route, state)) as Promise<
    boolean | UrlTree
  >;
}

describe('guestGuard', () => {
  let sessionStore: SessionStore;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    sessionStore = TestBed.inject(SessionStore);
  });

  it('permite acesso a /login quando anônimo (CA sem sessão)', async () => {
    sessionStore.finishBootstrap();

    expect(await runGuard()).toBe(true);
  });

  it('redireciona para /tasks quando já autenticado (CA-03)', async () => {
    sessionStore.startSession(
      { accessToken: 'abc', expiresAt: new Date().toISOString() },
      'a@b.com',
    );

    expect(await runGuard()).toBeInstanceOf(UrlTree);
  });

  it('espera o bootstrap: com a sessão restaurada, manda a /tasks em vez de mostrar o login', async () => {
    const pending = runGuard();

    sessionStore.updateTokens({ accessToken: 'abc', expiresAt: new Date().toISOString() });

    expect(await pending).toBeInstanceOf(UrlTree);
  });
});
