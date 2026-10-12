import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterStateSnapshot, UrlTree, ActivatedRouteSnapshot } from '@angular/router';

import { authGuard } from './auth.guard';
import { SessionStore } from './session-store';

function runGuard(url: string) {
  const route = {} as ActivatedRouteSnapshot;
  const state = { url } as RouterStateSnapshot;
  return TestBed.runInInjectionContext(() => authGuard(route, state)) as Promise<boolean | UrlTree>;
}

describe('authGuard', () => {
  let sessionStore: SessionStore;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    sessionStore = TestBed.inject(SessionStore);
  });

  it('permite navegação quando autenticado (CA-04)', async () => {
    sessionStore.startSession(
      { accessToken: 'abc', expiresAt: new Date().toISOString() },
      'a@b.com',
    );

    expect(await runGuard('/tasks')).toBe(true);
  });

  it('redireciona a /login preservando returnUrl quando anônimo (CA-01, CA-02)', async () => {
    sessionStore.finishBootstrap();

    const result = (await runGuard('/tasks/new')) as UrlTree;

    expect(result).toBeInstanceOf(UrlTree);
    const router = TestBed.inject(Router);
    expect(router.serializeUrl(result)).toBe('/login?returnUrl=%2Ftasks%2Fnew');
  });

  it('espera o bootstrap (status unknown) em vez de redirecionar cedo (FE-05, CA-06/CA-07)', async () => {
    let decided = false;
    const pending = runGuard('/tasks').then((result) => {
      decided = true;
      return result;
    });

    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(decided).toBe(false);

    sessionStore.updateTokens({ accessToken: 'abc', expiresAt: new Date().toISOString() });

    expect(await pending).toBe(true);
  });
});
