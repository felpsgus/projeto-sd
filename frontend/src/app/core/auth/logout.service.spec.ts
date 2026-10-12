import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { errorInterceptor } from '../errors/error.interceptor';
import { authInterceptor } from './auth.interceptor';
import { LogoutService } from './logout.service';
import { refreshInterceptor } from './refresh.interceptor';
import { SessionStore } from './session-store';

/** FE-10, CA-02 a CA-04, CA-08, CA-10. */
describe('LogoutService', () => {
  let service: LogoutService;
  let httpMock: HttpTestingController;
  let session: SessionStore;
  let router: Router;

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
    service = TestBed.inject(LogoutService);
    httpMock = TestBed.inject(HttpTestingController);
    session = TestBed.inject(SessionStore);
    router = TestBed.inject(Router);
    session.startSession(
      { accessToken: 'tok', expiresAt: new Date(Date.now() + 900_000).toISOString() },
      'a@b.com',
    );
  });

  afterEach(() => httpMock.verify());

  it('chama POST /api/auth/logout com corpo vazio e withCredentials, depois encerra e vai ao login com replaceUrl (CA-02/CA-04)', () => {
    const navigate = vi.spyOn(router, 'navigateByUrl');
    service.logout();

    const req = httpMock.expectOne('/api/auth/logout');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toBeNull();
    expect(req.request.withCredentials).toBe(true);
    expect(req.request.headers.get('Authorization')).toBe('Bearer tok');
    expect(session.isAuthenticated()).toBe(true);
    req.flush(null, { status: 204, statusText: 'No Content' });

    expect(session.isAuthenticated()).toBe(false);
    expect(session.accessToken()).toBeNull();
    expect(session.lastEndReason()).toBe('user_logout');
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
    expect(navigate).toHaveBeenCalledWith('/login', { replaceUrl: true });
  });

  it.each([500, 503])('a sessão local encerra mesmo se a API falhar com %i (CA-08)', (status) => {
    const navigate = vi.spyOn(router, 'navigateByUrl');
    service.logout();

    httpMock.expectOne('/api/auth/logout').flush({}, { status, statusText: 'Erro' });

    expect(session.isAuthenticated()).toBe(false);
    expect(session.lastEndReason()).toBe('user_logout');
    expect(navigate).toHaveBeenCalledWith('/login', { replaceUrl: true });
  });

  it('a sessão local encerra mesmo com a rede fora (CA-08)', () => {
    const navigate = vi.spyOn(router, 'navigateByUrl');
    service.logout();

    httpMock.expectOne('/api/auth/logout').error(new ProgressEvent('error'));

    expect(session.isAuthenticated()).toBe(false);
    expect(navigate).toHaveBeenCalledWith('/login', { replaceUrl: true });
  });

  it('logout-all chama POST /api/auth/logout-all e também encerra a sessão local (CA-10)', () => {
    const navigate = vi.spyOn(router, 'navigateByUrl');
    service.logoutAll();

    const req = httpMock.expectOne('/api/auth/logout-all');
    expect(req.request.body).toBeNull();
    expect(req.request.withCredentials).toBe(true);
    req.flush(null, { status: 204, statusText: 'No Content' });

    expect(session.isAuthenticated()).toBe(false);
    expect(navigate).toHaveBeenCalledWith('/login', { replaceUrl: true });
  });

  it('com o refresh recusado no meio do logout, encerra uma vez só, sem erro e sem repetir a navegação', async () => {
    const navigate = vi.spyOn(router, 'navigate');
    const navigateByUrl = vi.spyOn(router, 'navigateByUrl');
    service.logout();

    httpMock.expectOne('/api/auth/logout').flush({}, { status: 401, statusText: 'Unauthorized' });
    (await vi.waitFor(() => httpMock.expectOne('/api/auth/refresh'))).flush(
      {},
      { status: 401, statusText: 'Unauthorized' },
    );

    await vi.waitFor(() => expect(session.isAuthenticated()).toBe(false));
    expect(navigate).toHaveBeenCalledTimes(1);
    expect(navigateByUrl).not.toHaveBeenCalledWith('/login', { replaceUrl: true });
  });
});
