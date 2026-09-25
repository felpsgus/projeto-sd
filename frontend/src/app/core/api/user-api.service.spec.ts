import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { CHANGE_PASSWORD_PATH, ME_PATH, UserApi } from './user-api.service';
import { PROFILE_RESPONSE_FIXTURE } from '../../../testing/fixtures/user.fixtures';

describe('UserApi', () => {
  let userApi: UserApi;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    userApi = TestBed.inject(UserApi);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('faz GET em /api/me e devolve o perfil tipado (FE-11, CA-01)', () => {
    let result: unknown;
    userApi.getMe().subscribe((value) => (result = value));

    const req = httpMock.expectOne(ME_PATH);
    expect(req.request.method).toBe('GET');
    req.flush(PROFILE_RESPONSE_FIXTURE);

    expect(result).toEqual(PROFILE_RESPONSE_FIXTURE);
  });

  it('faz PATCH em /api/me só com displayName — sem email no corpo (FE-11, CA-05)', () => {
    userApi.updateProfile({ displayName: 'Novo Nome' }).subscribe();

    const req = httpMock.expectOne(ME_PATH);
    expect(req.request.method).toBe('PATCH');
    expect(req.request.body).toEqual({ displayName: 'Novo Nome' });
    expect(req.request.body.email).toBeUndefined();
    req.flush({ ...PROFILE_RESPONSE_FIXTURE, displayName: 'Novo Nome' });
  });

  it('faz POST em /api/me/change-password com as duas senhas (FE-12)', () => {
    userApi.changePassword({ currentPassword: 'old12345', newPassword: 'new123456' }).subscribe();

    const req = httpMock.expectOne(CHANGE_PASSWORD_PATH);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ currentPassword: 'old12345', newPassword: 'new123456' });
    req.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('faz DELETE em /api/me enviando a senha no corpo (FE-13, CA-20/CA-21 — segurança)', () => {
    userApi.deleteAccount({ password: 'secret12' }).subscribe();

    const req = httpMock.expectOne(ME_PATH);
    expect(req.request.method).toBe('DELETE');
    // O ponto de atenção do briefing: HttpClient exige `{ body }` explícito em DELETE —
    // sem isso a senha seria silenciosamente descartada.
    expect(req.request.body).toEqual({ password: 'secret12' });
    req.flush(null, { status: 204, statusText: 'No Content' });
  });
});
