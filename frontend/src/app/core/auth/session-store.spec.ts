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
    expect(store.accessTokenExpiresAt()).toBeNull();
    expect(store.email()).toBeNull();
    expect(store.lastEndReason()).toBe('user_logout');
  });

  it('nenhum token é gravado em localStorage ou sessionStorage após o login (CA-09/CA-10)', () => {
    store.startSession({ accessToken: 'abc-secret', expiresAt: '2026-01-01T00:15:00Z' }, 'a@b.com');

    const allLocalStorage = JSON.stringify({ ...localStorage });
    const allSessionStorage = JSON.stringify({ ...sessionStorage });

    expect(allLocalStorage).not.toContain('abc-secret');
    expect(allSessionStorage).not.toContain('abc-secret');
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
  });

  it('document.cookie não contém token algum depois de login e refresh (CA-12)', () => {
    store.startSession(
      { accessToken: 'tok-1-secret', expiresAt: '2026-01-01T00:15:00Z' },
      'a@b.com',
    );
    store.updateTokens({ accessToken: 'tok-2-secret', expiresAt: '2026-01-01T00:30:00Z' });

    expect(document.cookie).toBe('');
    expect(JSON.stringify({ ...localStorage })).not.toContain('secret');
    expect(JSON.stringify({ ...sessionStorage })).not.toContain('secret');
  });

  it('displayName é null até setDisplayName ser chamado (FE-11)', () => {
    store.startSession({ accessToken: 'abc', expiresAt: '2026-01-01T00:15:00Z' }, 'a@b.com');

    expect(store.displayName()).toBeNull();

    store.setDisplayName('Ana');

    expect(store.displayName()).toBe('Ana');
  });

  it('endSession limpa displayName junto com o resto da sessão (FE-11)', () => {
    store.startSession({ accessToken: 'abc', expiresAt: '2026-01-01T00:15:00Z' }, 'a@b.com');
    store.setDisplayName('Ana');

    store.endSession('user_logout');

    expect(store.displayName()).toBeNull();
  });

  it('aceita todos os motivos de encerramento e guarda o último (FE-05, CA-16)', () => {
    for (const reason of [
      'session_revoked',
      'password_changed',
      'account_deleted',
      'session_expired',
    ] as const) {
      store.startSession({ accessToken: 'abc', expiresAt: '2026-01-01T00:15:00Z' }, 'a@b.com');
      store.endSession(reason);
      expect(store.lastEndReason()).toBe(reason);
    }
  });

  it('status começa em unknown e deriva de token + bootstrap (FE-05)', () => {
    expect(store.status()).toBe('unknown');

    store.finishBootstrap();
    expect(store.status()).toBe('anonymous');

    store.startSession({ accessToken: 'abc', expiresAt: '2026-01-01T00:15:00Z' }, 'a@b.com');
    expect(store.status()).toBe('authenticated');

    store.endSession('user_logout');
    expect(store.status()).toBe('anonymous');
  });

  it('ready só resolve quando o bootstrap termina', async () => {
    let resolved = false;
    void store.ready.then(() => (resolved = true));
    await Promise.resolve();
    expect(resolved).toBe(false);

    store.finishBootstrap();
    await store.ready;
    expect(resolved).toBe(true);
  });

  it('updateTokens troca só o token e preserva e-mail e nome (FE-06, CA-08)', () => {
    store.startSession({ accessToken: 'old', expiresAt: '2026-01-01T00:15:00Z' }, 'a@b.com');
    store.setDisplayName('Ana');

    store.updateTokens({ accessToken: 'new', expiresAt: '2026-01-01T00:30:00Z' });

    expect(store.accessToken()).toBe('new');
    expect(store.accessTokenExpiresAt()).toEqual(new Date('2026-01-01T00:30:00Z'));
    expect(store.email()).toBe('a@b.com');
    expect(store.displayName()).toBe('Ana');
  });

  it('setProfile popula e-mail e nome depois da restauração por refresh', () => {
    store.updateTokens({ accessToken: 'new', expiresAt: '2026-01-01T00:30:00Z' });

    store.setProfile('a@b.com', 'Ana');

    expect(store.email()).toBe('a@b.com');
    expect(store.displayName()).toBe('Ana');
  });

  it('endSession local avisa os ouvintes; a remota não (sem eco entre abas)', () => {
    const listener = vi.fn();
    store.onLocalEnd(listener);

    store.endSession('user_logout');
    store.endSession('session_expired', 'remote');

    expect(listener).toHaveBeenCalledTimes(1);
    expect(listener).toHaveBeenCalledWith('user_logout');
  });
});
