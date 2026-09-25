import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
import { render, screen } from '@testing-library/angular';
import userEvent from '@testing-library/user-event';

import { LoginComponent } from './login.component';
import { SessionStore } from '../../../core/auth/session-store';
import { errorInterceptor } from '../../../core/errors/error.interceptor';

async function setup() {
  const utils = await render(LoginComponent, {
    providers: [
      provideHttpClient(withInterceptors([errorInterceptor])),
      provideHttpClientTesting(),
      provideRouter([{ path: 'tasks', children: [] }]),
    ],
  });
  const httpMock = utils.fixture.debugElement.injector.get(HttpTestingController);
  const sessionStore = utils.fixture.debugElement.injector.get(SessionStore);
  const router = utils.fixture.debugElement.injector.get(Router);
  return { ...utils, httpMock, sessionStore, router };
}

describe('LoginComponent', () => {
  it('exige e-mail e senha antes de enviar (CA-04)', async () => {
    const { httpMock } = await setup();

    await userEvent.click(screen.getByRole('button', { name: /entrar/i }));

    httpMock.expectNone('/api/auth/login');
  });

  it('login com credenciais válidas chama a API e inicia a sessão (CA-01)', async () => {
    const { httpMock, sessionStore } = await setup();

    await userEvent.type(screen.getByLabelText(/e-mail/i), 'user@example.com');
    await userEvent.type(screen.getByLabelText(/senha/i), 'secret123');
    await userEvent.click(screen.getByRole('button', { name: /entrar/i }));

    const req = httpMock.expectOne('/api/auth/login');
    req.flush({ accessToken: 'tok', expiresAt: new Date().toISOString() });

    expect(sessionStore.isAuthenticated()).toBe(true);
  });

  it('exibe "E-mail ou senha inválidos." tanto para senha errada quanto para e-mail inexistente, sem marcar campo (CA-05 a CA-08, CA-10)', async () => {
    for (const errorCode of ['auth.invalid_credentials']) {
      const { httpMock } = await setup();

      await userEvent.type(screen.getByLabelText(/e-mail/i), 'user@example.com');
      await userEvent.type(screen.getByLabelText(/senha/i), 'wrong');
      await userEvent.click(screen.getByRole('button', { name: /entrar/i }));

      const req = httpMock.expectOne('/api/auth/login');
      req.flush({ errorCode }, { status: 401, statusText: 'Unauthorized' });

      const alert = await screen.findByRole('alert');
      expect(alert.textContent).toContain('E-mail ou senha inválidos.');
      expect(screen.getByLabelText(/e-mail/i)).toHaveAttribute('aria-invalid', 'false');
    }
  });

  it('não existe link de "esqueci minha senha" (CA-18)', async () => {
    await setup();

    expect(screen.queryByText(/esqueci minha senha/i)).toBeNull();
  });

  it('usa autocomplete username/current-password (CA-20)', async () => {
    await setup();

    expect(screen.getByLabelText(/e-mail/i)).toHaveAttribute('autocomplete', 'username');
    expect(screen.getByLabelText(/senha/i)).toHaveAttribute('autocomplete', 'current-password');
  });

  it('pré-preenche o e-mail a partir do query param "email" (FE-08 CA-01, FE-12 CA-05)', async () => {
    await render(LoginComponent, {
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: 'tasks', children: [] }]),
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { queryParamMap: convertToParamMap({ email: 'nova@example.com' }) },
          },
        },
      ],
    });

    expect(screen.getByLabelText(/e-mail/i)).toHaveValue('nova@example.com');
  });

  it('exibe mensagem de cadastro concluído quando chega com "registered=1" (FE-08 CA-01)', async () => {
    await render(LoginComponent, {
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: 'tasks', children: [] }]),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { queryParamMap: convertToParamMap({ registered: '1' }) } },
        },
      ],
    });

    expect(screen.getByText(/conta criada com sucesso/i)).toBeTruthy();
  });

  it('exibe mensagem de senha alterada quando a sessão foi encerrada por "session_revoked" (FE-12, CA-02)', async () => {
    const seededSessionStore = new SessionStore();
    seededSessionStore.startSession(
      { accessToken: 'x', expiresAt: new Date().toISOString() },
      'a@b.com',
    );
    seededSessionStore.endSession('session_revoked');

    await render(LoginComponent, {
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: 'tasks', children: [] }]),
        { provide: SessionStore, useValue: seededSessionStore },
      ],
    });

    const message = screen.getByText(/senha foi alterada/i);
    expect(message.textContent).not.toMatch(/todos os dispositivos/i);
  });

  it('exibe mensagem de conta excluída quando a sessão foi encerrada por "account_deleted" (FE-13, CA-11)', async () => {
    const seededSessionStore = new SessionStore();
    seededSessionStore.startSession(
      { accessToken: 'x', expiresAt: new Date().toISOString() },
      'a@b.com',
    );
    seededSessionStore.endSession('account_deleted');

    await render(LoginComponent, {
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: 'tasks', children: [] }]),
        { provide: SessionStore, useValue: seededSessionStore },
      ],
    });

    expect(screen.getByText(/sua conta foi excluída/i)).toBeTruthy();
  });

  it('exibe "sua sessão expirou" quando chega por sessão expirada (CA-16)', async () => {
    // Sessão encerrada por expiração ANTES de a tela montar — reproduz o cenário real:
    // o interceptor de autenticação (FE-06) já chamou endSession antes de navegar ao login.
    const seededSessionStore = new SessionStore();
    seededSessionStore.startSession(
      { accessToken: 'x', expiresAt: new Date().toISOString() },
      'a@b.com',
    );
    seededSessionStore.endSession('session_expired');

    await render(LoginComponent, {
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: 'tasks', children: [] }]),
        { provide: SessionStore, useValue: seededSessionStore },
      ],
    });

    expect(screen.getByText(/sua sessão expirou/i)).toBeTruthy();
  });
});
