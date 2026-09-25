import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { render, screen } from '@testing-library/angular';
import userEvent from '@testing-library/user-event';

import { AccountComponent } from './account.component';
import { ME_PATH } from '../../core/api/user-api.service';
import { SessionStore } from '../../core/auth/session-store';
import { errorInterceptor } from '../../core/errors/error.interceptor';
import { TasksStore } from '../tasks/tasks.store';
import { PROFILE_RESPONSE_FIXTURE } from '../../../testing/fixtures/user.fixtures';

async function setup() {
  const utils = await render(AccountComponent, {
    providers: [
      provideHttpClient(withInterceptors([errorInterceptor])),
      provideHttpClientTesting(),
      provideRouter([{ path: 'login', children: [] }]),
    ],
  });
  const httpMock = utils.fixture.debugElement.injector.get(HttpTestingController);
  const session = utils.fixture.debugElement.injector.get(SessionStore);
  const router = utils.fixture.debugElement.injector.get(Router);
  session.startSession(
    { accessToken: 'tok', expiresAt: new Date().toISOString() },
    PROFILE_RESPONSE_FIXTURE.email,
  );
  return { ...utils, httpMock, session, router };
}

async function loadProfile(httpMock: HttpTestingController) {
  const req = httpMock.expectOne(ME_PATH);
  req.flush(PROFILE_RESPONSE_FIXTURE);
}

describe('AccountComponent (FE-11)', () => {
  it('mostra carregando e depois exibe e-mail, nome e data de criação (CA-01/CA-02/CA-12)', async () => {
    const { httpMock } = await setup();

    expect(screen.getByText(/carregando perfil/i)).toBeTruthy();

    await loadProfile(httpMock);

    expect(await screen.findByText(PROFILE_RESPONSE_FIXTURE.email)).toBeTruthy();
    expect(screen.getByDisplayValue('Ana')).toBeTruthy();
    expect(screen.getByText('15/01/2026')).toBeTruthy();
  });

  it('e-mail é somente leitura, com explicação — sem input de e-mail (CA-03/CA-04)', async () => {
    const { httpMock } = await setup();
    await loadProfile(httpMock);
    await screen.findByText(PROFILE_RESPONSE_FIXTURE.email);

    expect(screen.getByText(/não pode ser alterado/i)).toBeTruthy();
    expect(screen.queryByLabelText(/^e-mail$/i)).toBeNull();
  });

  it('PATCH não envia o campo email (CA-05)', async () => {
    const { httpMock } = await setup();
    await loadProfile(httpMock);
    await screen.findByDisplayValue('Ana');

    const nameInput = screen.getByLabelText(/nome de exibição/i);
    await userEvent.clear(nameInput);
    await userEvent.type(nameInput, 'Ana Paula');
    await userEvent.click(screen.getByRole('button', { name: /salvar/i }));

    const req = httpMock.expectOne(ME_PATH);
    expect(req.request.method).toBe('PATCH');
    expect(req.request.body).toEqual({ displayName: 'Ana Paula' });
    expect(req.request.body.email).toBeUndefined();
    req.flush({ ...PROFILE_RESPONSE_FIXTURE, displayName: 'Ana Paula' });
  });

  it('salvar o nome atualiza a tela e o cabeçalho na mesma fonte, sem recarregar (CA-06/CA-07)', async () => {
    const { httpMock, session } = await setup();
    await loadProfile(httpMock);
    await screen.findByDisplayValue('Ana');

    const nameInput = screen.getByLabelText(/nome de exibição/i);
    await userEvent.clear(nameInput);
    await userEvent.type(nameInput, 'Ana Paula');
    await userEvent.click(screen.getByRole('button', { name: /salvar/i }));

    const req = httpMock.expectOne(ME_PATH);
    req.flush({ ...PROFILE_RESPONSE_FIXTURE, displayName: 'Ana Paula' });

    expect(await screen.findByDisplayValue('Ana Paula')).toBeTruthy();
    // CA-07: o AppShell lê session.displayName() — provar que a MESMA fonte já mudou.
    expect(session.displayName()).toBe('Ana Paula');
  });

  it('nome vazio, só espaços ou 101 caracteres é rejeitado; 1 e 100 são aceitos (CA-08)', async () => {
    const { httpMock } = await setup();
    await loadProfile(httpMock);
    const nameInput = await screen.findByDisplayValue('Ana');

    await userEvent.clear(nameInput);
    await userEvent.type(nameInput, '   ');
    await userEvent.tab();
    expect(screen.getByRole('button', { name: /salvar/i })).toBeDisabled();

    await userEvent.clear(nameInput);
    await userEvent.type(nameInput, 'a'.repeat(101));
    await userEvent.tab();
    expect(screen.getByText(/no máximo 100 caracteres/i)).toBeTruthy();

    await userEvent.clear(nameInput);
    await userEvent.type(nameInput, 'a'.repeat(100));
    await userEvent.tab();
    expect(screen.queryByText(/no máximo 100 caracteres/i)).toBeNull();
    expect(screen.getByRole('button', { name: /salvar/i })).not.toBeDisabled();
  });

  it('nome é enviado com trim (CA-09)', async () => {
    const { httpMock } = await setup();
    await loadProfile(httpMock);
    const nameInput = await screen.findByDisplayValue('Ana');

    await userEvent.clear(nameInput);
    await userEvent.type(nameInput, '  Ana Paula  ');
    await userEvent.click(screen.getByRole('button', { name: /salvar/i }));

    const req = httpMock.expectOne(ME_PATH);
    expect(req.request.body).toEqual({ displayName: 'Ana Paula' });
    req.flush({ ...PROFILE_RESPONSE_FIXTURE, displayName: 'Ana Paula' });
  });

  it('salvar fica desabilitado sem alteração pendente e durante o envio (CA-10)', async () => {
    const { httpMock } = await setup();
    await loadProfile(httpMock);
    await screen.findByDisplayValue('Ana');

    expect(screen.getByRole('button', { name: /salvar/i })).toBeDisabled();
  });

  it('erro ao carregar mostra "tentar novamente" (CA-12)', async () => {
    const { httpMock } = await setup();

    const req = httpMock.expectOne(ME_PATH);
    req.flush({}, { status: 0, statusText: 'Unknown Error' });

    expect(await screen.findByRole('button', { name: /tentar novamente/i })).toBeTruthy();
  });

  it('alterar senha, sair e excluir conta estão acessíveis (CA-14/CA-15)', async () => {
    const { httpMock } = await setup();
    await loadProfile(httpMock);
    await screen.findByDisplayValue('Ana');

    expect(screen.getByRole('link', { name: /alterar senha/i })).toHaveAttribute(
      'href',
      '/account/password',
    );
    expect(screen.getByRole('button', { name: /^sair$/i })).toBeTruthy();
    expect(screen.getByRole('button', { name: /excluir minha conta/i })).toBeTruthy();
  });

  describe('exclusão de conta (FE-13)', () => {
    it('abre o diálogo ao clicar em "Excluir minha conta" (CA-01)', async () => {
      const { httpMock } = await setup();
      await loadProfile(httpMock);
      await screen.findByDisplayValue('Ana');

      await userEvent.click(screen.getByRole('button', { name: /excluir minha conta/i }));

      expect(screen.getByText(/excluir sua conta/i)).toBeTruthy();
    });

    it('confirmar com senha correta chama DELETE /api/me com a senha, encerra a sessão com account_deleted e navega para /login (CA-10/CA-11/CA-12)', async () => {
      const { httpMock, session, router } = await setup();
      const navigateSpy = vi.spyOn(router, 'navigateByUrl');
      await loadProfile(httpMock);
      await screen.findByDisplayValue('Ana');

      await userEvent.click(screen.getByRole('button', { name: /excluir minha conta/i }));
      await userEvent.type(screen.getByLabelText(/confirme sua senha/i), 'minhaSenha1');
      await userEvent.click(screen.getByRole('button', { name: /excluir permanentemente/i }));

      const req = httpMock.expectOne(ME_PATH);
      expect(req.request.method).toBe('DELETE');
      expect(req.request.body).toEqual({ password: 'minhaSenha1' });
      req.flush(null, { status: 204, statusText: 'No Content' });

      expect(session.isAuthenticated()).toBe(false);
      expect(session.accessToken()).toBeNull();
      expect(session.lastEndReason()).toBe('account_deleted');
      expect(navigateSpy).toHaveBeenCalledWith('/login', { replaceUrl: true });
    });

    it('após o sucesso o estado de tarefas é limpo (CA-15) e nenhuma chamada a /api/auth/logout é feita (CA-14)', async () => {
      const { httpMock, fixture } = await setup();
      const tasksStore = fixture.debugElement.injector.get(TasksStore);
      await loadProfile(httpMock);
      await screen.findByDisplayValue('Ana');

      await userEvent.click(screen.getByRole('button', { name: /excluir minha conta/i }));
      await userEvent.type(screen.getByLabelText(/confirme sua senha/i), 'minhaSenha1');
      await userEvent.click(screen.getByRole('button', { name: /excluir permanentemente/i }));

      const req = httpMock.expectOne(ME_PATH);
      req.flush(null, { status: 204, statusText: 'No Content' });

      expect(tasksStore.items().length).toBe(0);
      httpMock.expectNone('/api/auth/logout');
    });

    it('senha incorreta (400) mostra erro dentro do diálogo, que permanece aberto, e nada é excluído (CA-16/CA-17)', async () => {
      const { httpMock, session } = await setup();
      await loadProfile(httpMock);
      await screen.findByDisplayValue('Ana');

      await userEvent.click(screen.getByRole('button', { name: /excluir minha conta/i }));
      await userEvent.type(screen.getByLabelText(/confirme sua senha/i), 'senhaErrada');
      await userEvent.click(screen.getByRole('button', { name: /excluir permanentemente/i }));

      const req = httpMock.expectOne(ME_PATH);
      req.flush(
        { errorCode: 'auth.invalid_current_password', errors: { password: ['Senha incorreta.'] } },
        { status: 400, statusText: 'Bad Request' },
      );

      expect(await screen.findByRole('alert')).toHaveTextContent('Senha incorreta.');
      expect(screen.getByText(/excluir sua conta/i)).toBeTruthy();
      expect(session.isAuthenticated()).toBe(true);

      // CA-17: corrigir a senha e confirmar de novo funciona sem fechar/reabrir o diálogo.
      await userEvent.clear(screen.getByLabelText(/confirme sua senha/i));
      await userEvent.type(screen.getByLabelText(/confirme sua senha/i), 'senhaCorreta1');
      await userEvent.click(screen.getByRole('button', { name: /excluir permanentemente/i }));
      httpMock.expectOne(ME_PATH).flush(null, { status: 204, statusText: 'No Content' });
      expect(session.isAuthenticated()).toBe(false);
    });
  });
});
