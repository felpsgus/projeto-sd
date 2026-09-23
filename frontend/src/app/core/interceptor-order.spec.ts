import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { authInterceptor } from './auth/auth.interceptor';
import { SessionStore } from './auth/session-store';
import { AppError } from './errors/app-error.model';
import { errorInterceptor } from './errors/error.interceptor';

/**
 * FE-03, CA-12: a ordem dos interceptors é coberta por teste, não só documentada em
 * comentário. `errorInterceptor` precisa vir antes de `authInterceptor` no array de
 * `withInterceptors` (ver `app.config.ts`) para que ele só traduza o erro em `AppError`
 * DEPOIS que `authInterceptor` já reagiu ao 401 cru — nunca antes.
 */
describe('ordem dos interceptors (error antes de auth no array = auth processa o 401 cru primeiro)', () => {
  let httpClient: HttpClient;
  let httpMock: HttpTestingController;
  let sessionStore: SessionStore;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([errorInterceptor, authInterceptor])),
        provideHttpClientTesting(),
        provideRouter([{ path: 'login', children: [] }]),
      ],
    });
    httpClient = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    sessionStore = TestBed.inject(SessionStore);
  });

  afterEach(() => httpMock.verify());

  it('um 401 encerra a sessão (auth viu o HttpErrorResponse cru) e o assinante recebe um AppError (erro traduziu por último)', () => {
    sessionStore.startSession(
      { accessToken: 'abc', expiresAt: new Date().toISOString() },
      'a@b.com',
    );
    let captured: AppError | undefined;

    httpClient.get('/api/tasks').subscribe({ error: (error: AppError) => (captured = error) });

    const req = httpMock.expectOne('/api/tasks');
    req.flush({ errorCode: 'auth.unauthorized' }, { status: 401, statusText: 'Unauthorized' });

    expect(sessionStore.isAuthenticated()).toBe(false);
    expect(captured?.code).toBe('auth.unauthorized');
    expect(captured?.message).toBeTruthy();
  });
});
