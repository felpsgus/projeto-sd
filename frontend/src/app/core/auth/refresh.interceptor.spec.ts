import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { LoginResponse } from '../api/models/auth.models';
import { AppError } from '../errors/app-error.model';
import { errorInterceptor } from '../errors/error.interceptor';
import { authInterceptor } from './auth.interceptor';
import { refreshInterceptor } from './refresh.interceptor';
import { REFRESH_LOCK_NAME } from './session-refresher';
import { SessionStore } from './session-store';

const REFRESH = '/api/auth/refresh';
const inMinutes = (minutes: number) => new Date(Date.now() + minutes * 60 * 1000).toISOString();
const tokens = (accessToken: string, minutes = 15): LoginResponse => ({
  accessToken,
  expiresAt: inMinutes(minutes),
});
const unauthorized = { status: 401, statusText: 'Unauthorized' };

describe('refreshInterceptor (FE-06)', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let session: SessionStore;
  let router: Router;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        // Mesma ordem de app.config.ts: erro → refresh → auth (CA-19).
        provideHttpClient(
          withInterceptors([errorInterceptor, refreshInterceptor, authInterceptor]),
        ),
        provideHttpClientTesting(),
        provideRouter([
          { path: 'login', children: [] },
          { path: 'tasks', children: [] },
        ]),
      ],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    session = TestBed.inject(SessionStore);
    router = TestBed.inject(Router);
    await router.navigateByUrl('/tasks');
  });

  afterEach(() => {
    httpMock.verify();
    vi.restoreAllMocks();
  });

  /** Espera a chamada de refresh aparecer (o refresher é baseado em promessas) e a devolve. */
  async function takeRefreshRequests(): Promise<TestRequest[]> {
    return vi.waitFor(() => {
      const found = httpMock.match(REFRESH);
      expect(found.length).toBeGreaterThan(0);
      return found;
    });
  }

  /** Atalho para o caso comum: exatamente uma chamada de refresh. */
  async function takeRefreshRequest(): Promise<TestRequest> {
    const found = await takeRefreshRequests();
    expect(found).toHaveLength(1);
    return found[0] as TestRequest;
  }

  async function takeRequest(url: string): Promise<TestRequest> {
    return vi.waitFor(() => httpMock.expectOne(url));
  }

  describe('renovação reativa (401)', () => {
    it('três 401 simultâneos geram UMA chamada de refresh e três repetições com o token novo (CA-06/CA-07)', async () => {
      session.startSession(tokens('old'), 'a@b.com');
      const results: string[] = [];
      for (const url of ['/api/a', '/api/b', '/api/c']) {
        http.get<string>(url).subscribe((value) => results.push(value));
      }
      for (const url of ['/api/a', '/api/b', '/api/c']) {
        httpMock.expectOne(url).flush({}, unauthorized);
      }

      const refreshes = await takeRefreshRequests();
      expect(refreshes).toHaveLength(1);
      (refreshes[0] as TestRequest).flush(tokens('new'));

      for (const url of ['/api/a', '/api/b', '/api/c']) {
        const retry = await takeRequest(url);
        expect(retry.request.headers.get('Authorization')).toBe('Bearer new');
        retry.flush(url);
      }

      await vi.waitFor(() => expect(results).toHaveLength(3));
      expect(session.isAuthenticated()).toBe(true);
      expect(session.accessToken()).toBe('new');
      // O refresh não foi chamado de novo por nenhuma das repetições.
      expect(httpMock.match(REFRESH)).toHaveLength(0);
    });

    it('a chamada de refresh leva withCredentials, corpo vazio e nenhum Authorization (CA-05b/CA-05c)', async () => {
      session.startSession(tokens('old'), 'a@b.com');
      http.get('/api/a').subscribe();
      httpMock.expectOne('/api/a').flush({}, unauthorized);

      const refresh = await takeRefreshRequest();

      expect(refresh.request.method).toBe('POST');
      expect(refresh.request.withCredentials).toBe(true);
      expect(refresh.request.body).toBeNull();
      expect(refresh.request.headers.has('Authorization')).toBe(false);
      refresh.flush(tokens('new'));
      (await takeRequest('/api/a')).flush({});
    });

    it('grava o token novo no SessionStore (CA-08)', async () => {
      session.startSession(tokens('old', 1), 'a@b.com');
      const renewed = tokens('new', 15);
      http.get('/api/a').subscribe();
      httpMock.expectOne('/api/a').flush({}, unauthorized);

      (await takeRefreshRequest()).flush(renewed);
      (await takeRequest('/api/a')).flush({});

      expect(session.accessToken()).toBe('new');
      expect(session.accessTokenExpiresAt()).toEqual(new Date(renewed.expiresAt));
      expect(session.email()).toBe('a@b.com');
    });

    it('repete UMA vez: um segundo 401 na repetição não dispara novo refresh e a cadeia termina (CA-09/CA-10)', async () => {
      session.startSession(tokens('old'), 'a@b.com');
      let captured: AppError | undefined;
      http.get('/api/a').subscribe({ error: (error: AppError) => (captured = error) });
      httpMock.expectOne('/api/a').flush({}, unauthorized);

      (await takeRefreshRequest()).flush(tokens('new'));
      (await takeRequest('/api/a')).flush({}, unauthorized);

      await vi.waitFor(() => expect(captured?.status).toBe(401));
      expect(httpMock.match(REFRESH)).toHaveLength(0);
      // Sessão segue de pé: o servidor aceitou o refresh, o 401 é da rota.
      expect(session.isAuthenticated()).toBe(true);
    });

    it('um 401 de resposta atrasada, depois de outra requisição já ter renovado, só repete (sem novo refresh)', async () => {
      session.startSession(tokens('old'), 'a@b.com');
      http.get('/api/a').subscribe();
      const late = httpMock.expectOne('/api/a');

      session.updateTokens(tokens('new'));
      late.flush({}, unauthorized);

      const retry = await takeRequest('/api/a');
      expect(retry.request.headers.get('Authorization')).toBe('Bearer new');
      retry.flush({});
      expect(httpMock.match(REFRESH)).toHaveLength(0);
    });

    it('401 sem sessão não dispara refresh', () => {
      session.finishBootstrap();
      http.get('/api/a').subscribe({ error: () => undefined });

      httpMock.expectOne('/api/a').flush({}, unauthorized);

      httpMock.expectNone(REFRESH);
    });
  });

  describe('falha do refresh', () => {
    it('refresh 401 encerra a sessão, vai ao login preservando returnUrl e cancela a requisição (CA-11/CA-14/CA-15)', async () => {
      session.startSession(tokens('old'), 'a@b.com');
      const navigate = vi.spyOn(router, 'navigate');
      const onError = vi.fn();
      const onComplete = vi.fn();
      http.get('/api/a').subscribe({ error: onError, complete: onComplete });
      httpMock.expectOne('/api/a').flush({}, unauthorized);

      (await takeRefreshRequest()).flush({ errorCode: 'auth.invalid_refresh_token' }, unauthorized);

      await vi.waitFor(() => expect(onComplete).toHaveBeenCalled());
      expect(onError).not.toHaveBeenCalled();
      expect(session.isAuthenticated()).toBe(false);
      expect(session.status()).toBe('anonymous');
      expect(session.lastEndReason()).toBe('session_expired');
      expect(navigate).toHaveBeenCalledWith(['/login'], {
        queryParams: { returnUrl: '/tasks' },
        replaceUrl: true,
      });
    });

    it('refresh 401 com auth.refresh_token_revoked encerra com session_revoked (CA-12)', async () => {
      session.startSession(tokens('old'), 'a@b.com');
      http.get('/api/a').subscribe();
      httpMock.expectOne('/api/a').flush({}, unauthorized);

      (await takeRefreshRequest()).flush({ errorCode: 'auth.refresh_token_revoked' }, unauthorized);

      await vi.waitFor(() => expect(session.status()).toBe('anonymous'));
      expect(session.lastEndReason()).toBe('session_revoked');
    });

    it('três requisições com o refresh recusado encerram a sessão uma única vez', async () => {
      session.startSession(tokens('old'), 'a@b.com');
      const navigate = vi.spyOn(router, 'navigate');
      for (const url of ['/api/a', '/api/b', '/api/c']) {
        http.get(url).subscribe();
        httpMock.expectOne(url).flush({}, unauthorized);
      }

      const refreshes = await takeRefreshRequests();
      expect(refreshes).toHaveLength(1);
      (refreshes[0] as TestRequest).flush({}, unauthorized);

      await vi.waitFor(() => expect(session.isAuthenticated()).toBe(false));
      expect(navigate).toHaveBeenCalledTimes(1);
    });

    it.each([503, 0])(
      'refresh com falha transitória (%i) não encerra a sessão e o erro chega à tela',
      async (status) => {
        session.startSession(tokens('old'), 'a@b.com');
        let captured: AppError | undefined;
        http.get('/api/a').subscribe({ error: (error: AppError) => (captured = error) });
        httpMock.expectOne('/api/a').flush({}, unauthorized);

        const refresh = await takeRefreshRequest();
        if (status === 0) {
          refresh.error(new ProgressEvent('error'));
        } else {
          refresh.flush({}, { status, statusText: 'Service Unavailable' });
        }

        await vi.waitFor(() => expect(captured).toBeDefined());
        expect(session.isAuthenticated()).toBe(true);
        expect(session.lastEndReason()).toBeNull();
        expect(captured?.status).toBe(status);
      },
    );
  });

  describe('renovação proativa', () => {
    it('com o token expirando em menos de 30 s, renova ANTES de enviar e segue com o token novo (CA-05)', async () => {
      session.startSession(
        { accessToken: 'old', expiresAt: new Date(Date.now() + 10_000).toISOString() },
        'a@b.com',
      );
      http.get('/api/a').subscribe();

      httpMock.expectNone('/api/a');
      (await takeRefreshRequest()).flush(tokens('new'));

      const request = await takeRequest('/api/a');
      expect(request.request.headers.get('Authorization')).toBe('Bearer new');
      request.flush({});
    });

    it('com o token longe de expirar, não renova', () => {
      session.startSession(tokens('old'), 'a@b.com');
      http.get('/api/a').subscribe();

      httpMock.expectOne('/api/a').flush({});
      httpMock.expectNone(REFRESH);
    });

    it('se a renovação proativa falhar por erro transitório, a requisição segue com o token atual', async () => {
      session.startSession(
        { accessToken: 'old', expiresAt: new Date(Date.now() + 10_000).toISOString() },
        'a@b.com',
      );
      http.get('/api/a').subscribe();

      (await takeRefreshRequest()).flush({}, { status: 503, statusText: 'Unavailable' });

      const request = await takeRequest('/api/a');
      expect(request.request.headers.get('Authorization')).toBe('Bearer old');
      request.flush({});
      expect(session.isAuthenticated()).toBe(true);
    });

    it('requisições simultâneas com o token quase expirado compartilham um único refresh', async () => {
      session.startSession(
        { accessToken: 'old', expiresAt: new Date(Date.now() + 10_000).toISOString() },
        'a@b.com',
      );
      http.get('/api/a').subscribe();
      http.get('/api/b').subscribe();

      const refreshes = await takeRefreshRequests();
      expect(refreshes).toHaveLength(1);
      (refreshes[0] as TestRequest).flush(tokens('new'));

      (await takeRequest('/api/a')).flush({});
      (await takeRequest('/api/b')).flush({});
    });
  });

  describe('o que NÃO dispara refresh', () => {
    it.each([
      [403, 'Forbidden'],
      [404, 'Not Found'],
      [409, 'Conflict'],
    ])('um %i não dispara refresh nem encerra a sessão (CA-16/CA-17)', (status, statusText) => {
      session.startSession(tokens('old'), 'a@b.com');
      http.get('/api/a').subscribe({ error: () => undefined });

      httpMock.expectOne('/api/a').flush({}, { status, statusText });

      httpMock.expectNone(REFRESH);
      expect(session.isAuthenticated()).toBe(true);
    });

    it('erro de rede (status 0) não dispara refresh nem encerra a sessão (CA-18)', () => {
      session.startSession(tokens('old'), 'a@b.com');
      http.get('/api/a').subscribe({ error: () => undefined });

      httpMock.expectOne('/api/a').error(new ProgressEvent('error'));

      httpMock.expectNone(REFRESH);
      expect(session.isAuthenticated()).toBe(true);
    });

    it.each(['/api/auth/login', '/api/auth/register'])(
      'um 401 em %s (credencial errada) não dispara refresh',
      (path) => {
        session.startSession(tokens('old'), 'a@b.com');
        http.post(path, {}).subscribe({ error: () => undefined });

        httpMock.expectOne(path).flush({}, unauthorized);

        httpMock.expectNone(REFRESH);
        expect(session.isAuthenticated()).toBe(true);
      },
    );

    it('URL fora da API nunca passa pelo refresh', () => {
      session.startSession(tokens('old'), 'a@b.com');
      http.get('https://cdn.example.com/x.png').subscribe({ error: () => undefined });

      httpMock.expectOne('https://cdn.example.com/x.png').flush({}, unauthorized);

      httpMock.expectNone(REFRESH);
    });
  });

  describe('serialização entre abas (Web Locks)', () => {
    afterEach(() => {
      Reflect.deleteProperty(navigator, 'locks');
    });

    function mockLocks() {
      const queue: (() => void)[] = [];
      const request = vi.fn(
        (_name: string, callback: () => Promise<unknown>) =>
          new Promise((resolve, reject) => {
            queue.push(() => callback().then(resolve, reject));
          }),
      );
      Object.defineProperty(navigator, 'locks', { value: { request }, configurable: true });
      return { request, grant: () => queue.shift()?.() };
    }

    it('o refresh só sai quando o lock é concedido e usa o nome compartilhado', async () => {
      const locks = mockLocks();
      session.startSession(tokens('old'), 'a@b.com');
      http.get('/api/a').subscribe();
      httpMock.expectOne('/api/a').flush({}, unauthorized);

      await vi.waitFor(() => expect(locks.request).toHaveBeenCalledTimes(1));
      expect(locks.request.mock.calls[0]?.[0]).toBe(REFRESH_LOCK_NAME);
      // Outra aba segura o lock: nenhuma chamada de refresh ainda.
      expect(httpMock.match(REFRESH)).toHaveLength(0);

      locks.grant();
      const refresh = await takeRefreshRequest();
      refresh.flush(tokens('new'));
      (await takeRequest('/api/a')).flush({});
    });

    it('com vários 401 simultâneos, pega o lock uma vez só', async () => {
      const locks = mockLocks();
      session.startSession(tokens('old'), 'a@b.com');
      for (const url of ['/api/a', '/api/b', '/api/c']) {
        http.get(url).subscribe();
        httpMock.expectOne(url).flush({}, unauthorized);
      }

      await vi.waitFor(() => expect(locks.request).toHaveBeenCalled());
      expect(locks.request).toHaveBeenCalledTimes(1);

      locks.grant();
      (await takeRefreshRequest()).flush(tokens('new'));
      for (const url of ['/api/a', '/api/b', '/api/c']) {
        (await takeRequest(url)).flush({});
      }
    });
  });
});
