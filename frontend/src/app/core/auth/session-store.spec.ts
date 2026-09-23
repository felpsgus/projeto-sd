import { TestBed } from '@angular/core/testing';

import { SessionStore } from './session-store';

describe('SessionStore', () => {
  let store: SessionStore;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    store = TestBed.inject(SessionStore);
  });

  it('após startSession, isAuthenticated é true e os dados ficam disponíveis (CA-01)', () => {
    store.startSession({ accessToken: 'abc', expiresAt: '2026-01-01T00:15:00Z' }, 'a@b.com');

    expect(store.isAuthenticated()).toBe(true);
    expect(store.accessToken()).toBe('abc');
    expect(store.email()).toBe('a@b.com');
    expect(store.accessTokenExpiresAt()).toEqual(new Date('2026-01-01T00:15:00Z'));
  });

  it('após endSession, isAuthenticated é false e nada de sessão permanece (CA-02)', () => {
    store.startSession({ accessToken: 'abc', expiresAt: '2026-01-01T00:15:00Z' }, 'a@b.com');

    store.endSession('user_logout');

    expect(store.isAuthenticated()).toBe(false);
    expect(store.accessToken()).toBeNull();
    expect(store.email()).toBeNull();
    expect(store.lastEndReason()).toBe('user_logout');
  });

  it('nenhum token é gravado em localStorage ou sessionStorage após o login (segurança, FD-20)', () => {
    store.startSession({ accessToken: 'abc-secret', expiresAt: '2026-01-01T00:15:00Z' }, 'a@b.com');

    const allLocalStorage = JSON.stringify({ ...localStorage });
    const allSessionStorage = JSON.stringify({ ...sessionStorage });

    expect(allLocalStorage).not.toContain('abc-secret');
    expect(allSessionStorage).not.toContain('abc-secret');
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
  });

  it('document.cookie não contém o token (FD-20)', () => {
    store.startSession({ accessToken: 'abc-secret', expiresAt: '2026-01-01T00:15:00Z' }, 'a@b.com');

    expect(document.cookie).not.toContain('abc-secret');
  });
});
