import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { AuthApi, LOGIN_PATH } from './auth-api.service';
import { LoginResponse } from './models/auth.models';

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
});
