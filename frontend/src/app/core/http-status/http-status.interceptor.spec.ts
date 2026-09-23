import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { httpStatusInterceptor } from './http-status.interceptor';
import { HttpStatusService } from './http-status.service';

describe('httpStatusInterceptor', () => {
  let httpClient: HttpClient;
  let httpMock: HttpTestingController;
  let httpStatus: HttpStatusService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([httpStatusInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    httpClient = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    httpStatus = TestBed.inject(HttpStatusService);
  });

  afterEach(() => httpMock.verify());

  it('registra método, rota e status de uma resposta de sucesso', () => {
    httpClient.post('/api/tasks', {}).subscribe();
    httpMock.expectOne('/api/tasks').flush({}, { status: 201, statusText: 'Created' });

    expect(httpStatus.last()).toMatchObject({ method: 'POST', status: 201, ok: true });
  });

  it('registra também uma resposta de erro (ex.: 401)', () => {
    httpClient.get('/api/tasks').subscribe({ error: () => undefined });
    httpMock.expectOne('/api/tasks').flush({}, { status: 401, statusText: 'Unauthorized' });

    expect(httpStatus.last()).toMatchObject({ method: 'GET', status: 401, ok: false });
  });
});
