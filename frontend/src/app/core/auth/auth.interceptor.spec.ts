import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { provideRouter } from '@angular/router';

import { authInterceptor } from './auth.interceptor';
import { SessionStore } from './session-store';

describe('authInterceptor', () => {
  let httpClient: HttpClient;
  let httpMock: HttpTestingController;
  let sessionStore: SessionStore;
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        provideRouter([{ path: 'login', children: [] }]),
      ],
    });
    httpClient = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    sessionStore = TestBed.inject(SessionStore);
    router = TestBed.inject(Router);
  });

  afterEach(() => httpMock.verify());

  it('anexa Authorization quando há sessão ativa (CA-01)', () => {
    sessionStore.startSession(
      { accessToken: 'abc', expiresAt: new Date().toISOString() },
      'a@b.com',
    );

    httpClient.get('/api/tasks').subscribe();

    const req = httpMock.expectOne('/api/tasks');
    expect(req.request.headers.get('Authorization')).toBe('Bearer abc');
    req.flush({});
  });

  it('não anexa Authorization à requisição de login (CA-02)', () => {
    sessionStore.startSession(
      { accessToken: 'abc', expiresAt: new Date().toISOString() },
      'a@b.com',
    );

    httpClient.post('/api/auth/login', {}).subscribe();

    const req = httpMock.expectOne('/api/auth/login');
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush({});
  });

  it('sem sessão ativa, não envia Authorization vazio ou "Bearer null" (CA-04)', () => {
    httpClient.get('/api/tasks').subscribe({ error: () => undefined });

    const req = httpMock.expectOne('/api/tasks');
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush({}, { status: 401, statusText: 'Unauthorized' });
  });

  it('não anexa Authorization a URL fora da API (CA-03)', () => {
    sessionStore.startSession(
      { accessToken: 'abc', expiresAt: new Date().toISOString() },
      'a@b.com',
    );

    httpClient.get('https://cdn.example.com/logo.png').subscribe();

    const req = httpMock.expectOne('https://cdn.example.com/logo.png');
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush({});
  });

  it('um 401 numa chamada autenticada encerra a sessão e navega para /login', async () => {
    sessionStore.startSession(
      { accessToken: 'abc', expiresAt: new Date().toISOString() },
      'a@b.com',
    );
    const navigateSpy = vi.spyOn(router, 'navigateByUrl');

    httpClient.get('/api/tasks').subscribe({ error: () => undefined });
    const req = httpMock.expectOne('/api/tasks');
    req.flush({}, { status: 401, statusText: 'Unauthorized' });

    expect(sessionStore.isAuthenticated()).toBe(false);
    expect(sessionStore.lastEndReason()).toBe('session_expired');
    expect(navigateSpy).toHaveBeenCalledWith('/login');
  });

  it('um 403 não encerra a sessão (CA-16, distinção 401/403)', () => {
    sessionStore.startSession(
      { accessToken: 'abc', expiresAt: new Date().toISOString() },
      'a@b.com',
    );

    httpClient.get('/api/tasks').subscribe({ error: () => undefined });
    const req = httpMock.expectOne('/api/tasks');
    req.flush({}, { status: 403, statusText: 'Forbidden' });

    expect(sessionStore.isAuthenticated()).toBe(true);
  });
});
