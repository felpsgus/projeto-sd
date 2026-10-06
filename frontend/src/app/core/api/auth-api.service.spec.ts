import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { readFileSync } from 'node:fs';

import { AuthApi, LOGIN_PATH, REGISTER_PATH } from './auth-api.service';
import { LoginResponse } from './models/auth.models';
import { RegisterResponse } from './models/user.models';
import { LOGIN_RESPONSE_FIXTURE } from '../../../testing/fixtures/login.fixtures';
import { TASK_RESPONSE_FIXTURE } from '../../../testing/fixtures/task.fixtures';

describe('AuthApi', () => {
  let authApi: AuthApi;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    authApi = TestBed.inject(AuthApi);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('faz POST em /api/auth/login com o corpo e devolve a resposta tipada', () => {
    const response: LoginResponse = { accessToken: 'token-123', expiresAt: '2026-01-01T00:15:00Z' };
    let result: LoginResponse | undefined;

    authApi.login({ email: 'a@b.com', password: 'secret' }).subscribe((value) => (result = value));

    const req = httpMock.expectOne(LOGIN_PATH);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ email: 'a@b.com', password: 'secret' });
    req.flush(response);

    expect(result).toEqual(response);
  });

  it('faz POST em /api/auth/register com o corpo e devolve a resposta tipada (FE-08)', () => {
    const response: RegisterResponse = {
      id: 'id-1',
      email: 'a@b.com',
      displayName: 'a',
      createdAt: '2026-01-01T00:00:00Z',
    };
    let result: RegisterResponse | undefined;

    authApi
      .register({ email: 'a@b.com', password: 'secret12' })
      .subscribe((value) => (result = value));

    const req = httpMock.expectOne(REGISTER_PATH);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ email: 'a@b.com', password: 'secret12' });
    req.flush(response);

    expect(result).toEqual(response);
  });

  // FE-02, CA-17 (FD-16): todas as chamadas /api/auth/* levam o cookie de refresh.
  it('envia withCredentials em login, register, refresh, logout e logout-all (CA-17)', () => {
    authApi.login({ email: 'a@b.com', password: 'x' }).subscribe();
    authApi.register({ email: 'a@b.com', password: 'x' }).subscribe();
    authApi.refresh().subscribe();
    authApi.logout().subscribe();
    authApi.logoutAll().subscribe();

    const reqs = httpMock.match((r) => r.url.startsWith('/api/auth/'));
    expect(reqs.length).toBe(5);
    expect(reqs.map((r) => r.request.withCredentials)).toEqual([true, true, true, true, true]);
    reqs.forEach((r) => r.flush(null));
  });
});

/** Campos (camelCase, como o Gateway serializa) do `record` C# de contrato do Gateway. */
function gatewayRecordFields(record: string): string[] {
  const source = readFileSync(
    `../src/Gateway/TodoList.Gateway.Api/Contracts/${record}.cs`,
    'utf-8',
  );
  const params = /record\s+\w+\(([^)]*)\)/.exec(source)?.[1] ?? '';
  return params
    .split(',')
    .map((p) => p.trim().split(/\s+/).pop() ?? '')
    .filter(Boolean)
    .map((n) => n.charAt(0).toLowerCase() + n.slice(1))
    .sort();
}

// FE-02, CA-11: se um DTO do Gateway ganhar/perder campo, estes testes quebram.
describe('contrato com o Gateway', () => {
  it('LOGIN_RESPONSE_FIXTURE espelha LoginHttpResponse', () => {
    expect(Object.keys(LOGIN_RESPONSE_FIXTURE).sort()).toEqual(
      gatewayRecordFields('LoginHttpResponse'),
    );
  });

  it('TASK_RESPONSE_FIXTURE espelha TaskHttpResponse', () => {
    expect(Object.keys(TASK_RESPONSE_FIXTURE).sort()).toEqual(
      gatewayRecordFields('TaskHttpResponse'),
    );
  });
});
