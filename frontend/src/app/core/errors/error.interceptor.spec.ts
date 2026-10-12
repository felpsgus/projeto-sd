import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { errorInterceptor } from './error.interceptor';
import { AppError } from './app-error.model';

describe('errorInterceptor', () => {
  let httpClient: HttpClient;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    httpClient = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('converte um HttpErrorResponse em AppError para quem assina a chamada', () => {
    let captured: AppError | undefined;

    httpClient.post('/api/auth/login', {}).subscribe({
      error: (error: AppError) => (captured = error),
    });

    const req = httpMock.expectOne('/api/auth/login');
    req.flush(
      { errorCode: 'auth.invalid_credentials' },
      { status: 401, statusText: 'Unauthorized' },
    );

    expect(captured?.code).toBe('auth.invalid_credentials');
    expect(captured?.message).toBe('E-mail ou senha inválidos.');
  });
});
