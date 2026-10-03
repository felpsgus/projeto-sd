import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { authInterceptor } from './auth.interceptor';
import { SessionStore } from './session-store';

const IN_15_MIN = () => new Date(Date.now() + 15 * 60 * 1000).toISOString();

/** FE-06, CA-01 a CA-04: o `authInterceptor` só anexa o Bearer; a renovação é do `refreshInterceptor`. */
describe('authInterceptor', () => {
  let httpClient: HttpClient;
  let httpMock: HttpTestingController;
  let sessionStore: SessionStore;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    });
    httpClient = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    sessionStore = TestBed.inject(SessionStore);
  });

  afterEach(() => httpMock.verify());

  it('anexa Authorization quando há sessão ativa (CA-01)', () => {
    sessionStore.startSession({ accessToken: 'abc', expiresAt: IN_15_MIN() }, 'a@b.com');

    httpClient.get('/api/tasks').subscribe();

    const req = httpMock.expectOne('/api/tasks');
    expect(req.request.headers.get('Authorization')).toBe('Bearer abc');
    req.flush({});
  });

  it.each(['/api/auth/login', '/api/auth/register', '/api/auth/refresh'])(
    'não anexa Authorization a %s (CA-02)',
    (path) => {
      sessionStore.startSession({ accessToken: 'abc', expiresAt: IN_15_MIN() }, 'a@b.com');

      httpClient.post(path, {}).subscribe();

      const req = httpMock.expectOne(path);
      expect(req.request.headers.has('Authorization')).toBe(false);
      req.flush({});
    },
  );

  it('anexa Authorization a logout e logout-all', () => {
    sessionStore.startSession({ accessToken: 'abc', expiresAt: IN_15_MIN() }, 'a@b.com');

    httpClient.post('/api/auth/logout', null).subscribe();
    httpClient.post('/api/auth/logout-all', null).subscribe();

    for (const req of [
      httpMock.expectOne('/api/auth/logout'),
      httpMock.expectOne('/api/auth/logout-all'),
    ]) {
      expect(req.request.headers.get('Authorization')).toBe('Bearer abc');
      req.flush(null, { status: 204, statusText: 'No Content' });
    }
  });

  it('sem sessão ativa, não envia Authorization vazio ou "Bearer null" (CA-04)', () => {
    httpClient.get('/api/tasks').subscribe();

    const req = httpMock.expectOne('/api/tasks');
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush({});
  });

  it('não anexa Authorization a URL fora da API (CA-03)', () => {
    sessionStore.startSession({ accessToken: 'abc', expiresAt: IN_15_MIN() }, 'a@b.com');

    httpClient.get('https://cdn.example.com/logo.png').subscribe();

    const req = httpMock.expectOne('https://cdn.example.com/logo.png');
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush({});
  });
});
