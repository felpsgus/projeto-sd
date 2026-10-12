import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { authInterceptor } from './auth/auth.interceptor';
import { refreshInterceptor } from './auth/refresh.interceptor';
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
        provideHttpClient(
          withInterceptors([errorInterceptor, refreshInterceptor, authInterceptor]),
        ),
        provideHttpClientTesting(),
        provideRouter([{ path: 'login', children: [] }]),
      ],
    });
    httpClient = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    sessionStore = TestBed.inject(SessionStore);
  });

  afterEach(() => httpMock.verify());

  it('um 401 passa CRU pelo refresh (que renova e repete) e só depois vira AppError para o assinante (FE-06, CA-19)', async () => {
    sessionStore.startSession(
      { accessToken: 'abc', expiresAt: new Date(Date.now() + 900_000).toISOString() },
      'a@b.com',
    );
    let captured: AppError | undefined;

    httpClient.get('/api/tasks').subscribe({ error: (error: AppError) => (captured = error) });

    httpMock.expectOne('/api/tasks').flush({}, { status: 401, statusText: 'Unauthorized' });
    // Se o erro traduzisse ANTES do refresh, o AppError chegaria aqui sem nenhum refresh.
    expect(captured).toBeUndefined();
    const refresh = await vi.waitFor(() => httpMock.expectOne('/api/auth/refresh'));
    refresh.flush({ accessToken: 'novo', expiresAt: new Date(Date.now() + 900_000).toISOString() });

    const retry = await vi.waitFor(() => httpMock.expectOne('/api/tasks'));
    expect(retry.request.headers.get('Authorization')).toBe('Bearer novo');
    retry.flush({ errorCode: 'auth.unauthorized' }, { status: 401, statusText: 'Unauthorized' });

    await vi.waitFor(() => expect(captured?.code).toBe('auth.unauthorized'));
    expect(captured?.message).toBeTruthy();
  });
});
