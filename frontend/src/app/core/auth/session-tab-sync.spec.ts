import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { SessionStore } from './session-store';
import { SessionTabSync } from './session-tab-sync';

/** FE-05, CA-15: encerrar numa aba encerra nas outras. */
describe('SessionTabSync', () => {
  let session: SessionStore;
  let otherTab: BroadcastChannel;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideRouter([{ path: 'login', children: [] }])],
    });
    session = TestBed.inject(SessionStore);
    TestBed.inject(SessionTabSync).start();
    otherTab = new BroadcastChannel('todolist-session');
  });

  afterEach(() => otherTab.close());

  it('um encerramento nesta aba é anunciado às outras (só o motivo, nenhum token)', async () => {
    const received: unknown[] = [];
    otherTab.onmessage = (event) => received.push(event.data);
    session.startSession(
      { accessToken: 'segredo', expiresAt: new Date().toISOString() },
      'a@b.com',
    );

    session.endSession('user_logout');

    await vi.waitFor(() => expect(received).toEqual(['user_logout']));
  });

  it('um encerramento vindo de outra aba encerra esta, leva ao login e não ecoa de volta', async () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl');
    const echoes: unknown[] = [];
    session.startSession({ accessToken: 'abc', expiresAt: new Date().toISOString() }, 'a@b.com');
    otherTab.onmessage = (event) => echoes.push(event.data);

    otherTab.postMessage('session_expired');

    await vi.waitFor(() => expect(session.isAuthenticated()).toBe(false));
    expect(session.lastEndReason()).toBe('session_expired');
    expect(navigate).toHaveBeenCalledWith('/login', { replaceUrl: true });
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(echoes).toEqual([]);
  });

  it('uma aba já anônima ignora a mensagem', async () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl');
    session.finishBootstrap();

    otherTab.postMessage('user_logout');

    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(navigate).not.toHaveBeenCalled();
    expect(session.lastEndReason()).toBeNull();
  });
});
