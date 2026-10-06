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

async function setupWithReturnUrl(returnUrl: string) {
  const utils = await render(LoginComponent, {
    providers: [
      provideHttpClient(withInterceptors([errorInterceptor])),
      provideHttpClientTesting(),
      provideRouter([]),
      {
        provide: ActivatedRoute,
        useValue: { snapshot: { queryParamMap: convertToParamMap({ returnUrl }) } },
      },
    ],
  });
  const httpMock = utils.fixture.debugElement.injector.get(HttpTestingController);
  const router = utils.fixture.debugElement.injector.get(Router);
  return { ...utils, httpMock, router };
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

  // FE-09, CA-10 (guardião de RN-AUTH-09): senha errada e e-mail inexistente chegam como
  // 401 que só difere no corpo (title, detail, traceId) — o DOM do erro tem de ser idêntico.
  describe('falhas de credencial (CA-10)', () => {
    const rendered: string[] = [];

    it.each([
      [
        'senha errada',
        { errorCode: 'auth.invalid_credentials', title: 'senha errada', traceId: 'a' },
      ],
      [
        'e-mail inexistente',
        { errorCode: 'auth.invalid_credentials', title: 'sem e-mail', traceId: 'b' },
      ],
      [
        'corpo só com detail',
        { errorCode: 'auth.invalid_credentials', detail: 'outro texto', traceId: 'c' },
      ],
    ])('%s mostra a mensagem única', async (_name, body) => {
      const { httpMock, fixture } = await setup();
      await userEvent.type(screen.getByLabelText(/e-mail/i), 'user@example.com');
      await userEvent.type(screen.getByLabelText(/senha/i), 'wrong');
      await userEvent.click(screen.getByRole('button', { name: /entrar/i }));
      httpMock
        .expectOne('/api/auth/login')
        .flush(body, { status: 401, statusText: 'Unauthorized' });
      fixture.detectChanges();

      const alert = await screen.findByRole('alert');
      expect(alert.textContent?.trim()).toBe('E-mail ou senha inválidos.');
      rendered.push(alert.outerHTML.replace(/\s+/g, ' '));
    });

    it('o DOM renderizado é idêntico nos três cenários', () => {
      expect(rendered.length).toBe(3);
      expect(new Set(rendered).size).toBe(1);
    });
  });

  // FE-09, CA-03
  it('desabilita o botão durante o envio e o clique duplo dispara uma única chamada (CA-03)', async () => {
    const { httpMock, fixture } = await setup();
    await userEvent.type(screen.getByLabelText(/e-mail/i), 'user@example.com');
    await userEvent.type(screen.getByLabelText(/senha/i), 'secret123');

    await userEvent.dblClick(screen.getByRole('button', { name: /entrar/i }));
    fixture.detectChanges();

    expect(screen.getByRole('button', { name: /entrando/i })).toBeDisabled();
    httpMock.expectOne('/api/auth/login');
  });

  // FE-09, CA-21
  it('anuncia o erro (alert/aria-live) e leva o foco até ele (CA-21)', async () => {
    const { httpMock, fixture } = await setup();
    await userEvent.type(screen.getByLabelText(/e-mail/i), 'user@example.com');
    await userEvent.type(screen.getByLabelText(/senha/i), 'wrong');
    await userEvent.click(screen.getByRole('button', { name: /entrar/i }));
    httpMock
      .expectOne('/api/auth/login')
      .flush(
        { errorCode: 'auth.invalid_credentials' },
        { status: 401, statusText: 'Unauthorized' },
      );
    fixture.detectChanges();
    await fixture.whenStable();

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveAttribute('aria-live', 'assertive');
    await vi.waitFor(() => expect(alert).toHaveFocus());
  });

  // FE-09, CA-02 / FE-07, CA-08 e CA-10: após autenticar, vai para a returnUrl — inclusive
  // uma rota interna inexistente (que a rota curinga trata como 404).
  it.each(['/tasks/abc/edit', '/nao/existe'])(
    'após o login navega para a returnUrl %s (FE-09 CA-02, FE-07 CA-08/CA-10)',
    async (returnUrl) => {
      const { httpMock, router } = await setupWithReturnUrl(returnUrl);
      const navigate = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);

      await userEvent.type(screen.getByLabelText(/e-mail/i), 'user@example.com');
      await userEvent.type(screen.getByLabelText(/senha/i), 'secret123');
      await userEvent.click(screen.getByRole('button', { name: /entrar/i }));
      httpMock
        .expectOne('/api/auth/login')
        .flush({ accessToken: 'tok', expiresAt: new Date().toISOString() });

      expect(navigate).toHaveBeenCalledWith(returnUrl);
    },
  );

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

  it('exibe mensagem de senha alterada quando a sessão foi encerrada por "password_changed" (FE-12, CA-02)', async () => {
    const seededSessionStore = new SessionStore();
    seededSessionStore.startSession(
      { accessToken: 'x', expiresAt: new Date().toISOString() },
      'a@b.com',
    );
    seededSessionStore.endSession('password_changed');

    await render(LoginComponent, {
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: 'tasks', children: [] }]),
        { provide: SessionStore, useValue: seededSessionStore },
      ],
    });

    expect(screen.getByText(/senha foi alterada/i)).toBeTruthy();
  });

  it.each([
    ['session_revoked', /sua sessão foi encerrada/i],
    ['user_logout', /você saiu/i],
  ] as const)('exibe a mensagem de contexto de "%s" (FE-05, CA-16)', async (reason, pattern) => {
    const seededSessionStore = new SessionStore();
    seededSessionStore.startSession(
      { accessToken: 'x', expiresAt: new Date().toISOString() },
      'a@b.com',
    );
    seededSessionStore.endSession(reason);

    await render(LoginComponent, {
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: 'tasks', children: [] }]),
        { provide: SessionStore, useValue: seededSessionStore },
      ],
    });

    const message = screen.getByRole('status');
    expect(message.textContent).toMatch(pattern);
    expect(screen.queryByRole('alert')).toBeNull();
  });

  // Bloqueio de login (RN-AUTH-13, FE-09 CA-12/CA-13): o bloqueio é do E-MAIL, não do navegador.
  // O botão fica desabilitado só enquanto o campo contém o e-mail bloqueado E o tempo não
  // acabou; a mensagem é recontada por minuto. Sem Retry-After não há como saber quando
  // reabilitar, então o botão segue habilitado (CA-14).
  describe('bloqueio por tentativas (429)', () => {
    afterEach(() => vi.useRealTimers());

    async function setupBlocked(retryAfter: string | null = '540') {
      vi.useFakeTimers();
      const utils = await setup();
      const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime.bind(vi) });
      await user.type(screen.getByLabelText(/e-mail/i), 'user@example.com');
      await user.type(screen.getByLabelText(/senha/i), 'secret123');
      await user.click(screen.getByRole('button', { name: /entrar/i }));
      utils.httpMock.expectOne('/api/auth/login').flush(
        { errorCode: 'auth.too_many_attempts' },
        {
          status: 429,
          statusText: 'Too Many Requests',
          headers: retryAfter ? { 'Retry-After': retryAfter } : {},
        },
      );
      utils.fixture.detectChanges();
      return { ...utils, user };
    }

    async function tick(seconds: number, fixture: { detectChanges(): void }) {
      await vi.advanceTimersByTimeAsync(seconds * 1000);
      fixture.detectChanges();
    }

    const button = () => screen.getByRole('button', { name: /entrar/i });

    it('desabilita o botão e mostra o tempo em minutos', async () => {
      await setupBlocked();

      expect(screen.getByRole('alert').textContent).toContain(
        'Muitas tentativas. Tente novamente em 9 minutos.',
      );
      expect(button()).toBeDisabled();
    });

    it('conta regressiva por minuto, sem reabilitar antes do fim', async () => {
      const { fixture } = await setupBlocked();

      await tick(60, fixture);

      expect(screen.getByRole('alert').textContent).toContain('em 8 minutos');
      expect(button()).toBeDisabled();
    });

    it('ao fim do tempo reabilita o botão e a mensagem some', async () => {
      const { fixture } = await setupBlocked();

      await tick(540, fixture);

      expect(button()).not.toBeDisabled();
      expect(screen.queryByRole('alert')).toBeNull();
    });

    it('trocar o e-mail reabilita; voltar ao e-mail bloqueado (normalizado) desabilita', async () => {
      const { fixture, user } = await setupBlocked();
      const email = screen.getByLabelText(/e-mail/i);

      await user.clear(email);
      await user.type(email, 'outro@example.com');
      fixture.detectChanges();
      expect(button()).not.toBeDisabled();

      await user.clear(email);
      await user.type(email, 'USER@example.com ');
      fixture.detectChanges();
      expect(button()).toBeDisabled();
    });

    it('sem Retry-After: mensagem sem número e botão habilitado', async () => {
      await setupBlocked(null);

      expect(screen.getByRole('alert').textContent).toContain(
        'Muitas tentativas. Tente novamente em alguns minutos.',
      );
      expect(button()).not.toBeDisabled();
    });

    it('Enter durante o bloqueio não dispara requisição', async () => {
      const { httpMock, user } = await setupBlocked();

      await user.type(screen.getByLabelText(/senha/i), '{Enter}');

      httpMock.expectNone('/api/auth/login');
    });

    it('destruir o componente não deixa timer pendente', async () => {
      const { fixture } = await setupBlocked();
      expect(vi.getTimerCount()).toBeGreaterThan(0);

      fixture.destroy();
      await vi.advanceTimersByTimeAsync(0); // deixa o agendador do Angular esvaziar o que é dele

      expect(vi.getTimerCount()).toBe(0);
    });
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
