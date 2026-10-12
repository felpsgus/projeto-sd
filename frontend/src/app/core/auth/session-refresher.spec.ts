import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { ME_PATH } from '../api/user-api.service';
import { errorInterceptor } from '../errors/error.interceptor';
import { PROFILE_RESPONSE_FIXTURE } from '../../../testing/fixtures/user.fixtures';
import { authInterceptor } from './auth.interceptor';
import { refreshInterceptor } from './refresh.interceptor';
import { SessionRefresher } from './session-refresher';
import { SessionStore } from './session-store';

const REFRESH = '/api/auth/refresh';
const fresh = (accessToken: string) => ({
  accessToken,
  expiresAt: new Date(Date.now() + 15 * 60 * 1000).toISOString(),
});

/** FE-05, CA-06 a CA-08: bootstrap por refresh antes de as rotas ativarem. */
describe('SessionRefresher.restore (bootstrap)', () => {
  let refresher: SessionRefresher;
  let httpMock: HttpTestingController;
  let session: SessionStore;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(
          withInterceptors([errorInterceptor, refreshInterceptor, authInterceptor]),
        ),
        provideHttpClientTesting(),
        provideRouter([{ path: 'login', children: [] }]),
      ],
    });
    refresher = TestBed.inject(SessionRefresher);
    httpMock = TestBed.inject(HttpTestingController);
    session = TestBed.inject(SessionStore);
  });

  afterEach(() => httpMock.verify());

  it('status é unknown durante a restauração e authenticated depois, com e-mail e nome de /api/me (CA-06/CA-07)', async () => {
    const done = refresher.restore();
    expect(session.status()).toBe('unknown');

    const refresh = await vi.waitFor(() => httpMock.expectOne(REFRESH));
    expect(refresh.request.withCredentials).toBe(true);
    expect(refresh.request.headers.has('Authorization')).toBe(false);
    refresh.flush(fresh('restored'));

    const me = await vi.waitFor(() => httpMock.expectOne(ME_PATH));
    expect(me.request.headers.get('Authorization')).toBe('Bearer restored');
    me.flush(PROFILE_RESPONSE_FIXTURE);
    await done;

    expect(session.status()).toBe('authenticated');
    expect(session.email()).toBe(PROFILE_RESPONSE_FIXTURE.email);
    expect(session.displayName()).toBe(PROFILE_RESPONSE_FIXTURE.displayName);
  });

  it('refresh recusado vira anonymous, sem mensagem de erro e sem navegar (CA-08)', async () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate');
    const done = refresher.restore();

    (await vi.waitFor(() => httpMock.expectOne(REFRESH))).flush(
      { errorCode: 'auth.invalid_refresh_token' },
      { status: 401, statusText: 'Unauthorized' },
    );
    await done;

    expect(session.status()).toBe('anonymous');
    expect(session.lastEndReason()).toBeNull();
    expect(navigate).not.toHaveBeenCalled();
    httpMock.expectNone(ME_PATH);
  });

  it('servidor fora (503) no bootstrap também termina em anonymous, sem alarde', async () => {
    const done = refresher.restore();

    (await vi.waitFor(() => httpMock.expectOne(REFRESH))).flush(
      {},
      { status: 503, statusText: 'Service Unavailable' },
    );
    await done;

    expect(session.status()).toBe('anonymous');
    expect(session.lastEndReason()).toBeNull();
  });

  it('se /api/me falhar depois do refresh, a sessão restaurada continua de pé', async () => {
    const done = refresher.restore();
    (await vi.waitFor(() => httpMock.expectOne(REFRESH))).flush(fresh('restored'));

    (await vi.waitFor(() => httpMock.expectOne(ME_PATH))).flush(
      {},
      { status: 500, statusText: 'Server Error' },
    );
    await done;

    expect(session.status()).toBe('authenticated');
  });
});
