import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { clientDateInterceptor } from './client-date.interceptor';
import { CLIENT_DATE_HEADER } from './client-date.util';

describe('clientDateInterceptor', () => {
  let httpClient: HttpClient;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([clientDateInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    httpClient = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('anexa X-Client-Date no formato yyyy-MM-dd a uma requisição da API (CA-13)', () => {
    httpClient.get('/api/tasks').subscribe();

    const req = httpMock.expectOne('/api/tasks');
    expect(req.request.headers.get(CLIENT_DATE_HEADER)).toMatch(/^\d{4}-\d{2}-\d{2}$/);
    req.flush({});
  });

  it('não anexa o header a uma URL fora da API (CA-14)', () => {
    httpClient.get('https://cdn.example.com/logo.png').subscribe();

    const req = httpMock.expectOne('https://cdn.example.com/logo.png');
    expect(req.request.headers.has(CLIENT_DATE_HEADER)).toBe(false);
    req.flush({});
  });
});
