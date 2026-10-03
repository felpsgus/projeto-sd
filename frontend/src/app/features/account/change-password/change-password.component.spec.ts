import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { render, screen } from '@testing-library/angular';
import userEvent from '@testing-library/user-event';

import { ChangePasswordComponent } from './change-password.component';
import { CHANGE_PASSWORD_PATH } from '../../../core/api/user-api.service';
import { SessionStore } from '../../../core/auth/session-store';
import { errorInterceptor } from '../../../core/errors/error.interceptor';

async function setup() {
  const utils = await render(ChangePasswordComponent, {
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
    'ana@example.com',
  );
  return { ...utils, httpMock, session, router };
}

async function fillValidForm() {
  await userEvent.type(screen.getByLabelText(/senha atual/i), 'senhaAtual1');
  await userEvent.type(screen.getByLabelText(/^nova senha$/i), 'novaSenha1');
  await userEvent.type(screen.getByLabelText(/confirmar nova senha/i), 'novaSenha1');
}

describe('ChangePasswordComponent (FE-12)', () => {
  it('o aviso de desconexão aparece antes do envio (CA-06) com texto verdadeiro (sem "todos os dispositivos")', async () => {
    await setup();

    const notice = screen.getByRole('status');
    expect(notice.textContent).toMatch(/desconectado/i);
    expect(notice.textContent).not.toMatch(/todos os dispositivos/i);
  });

  it('troca válida chama a API, encerra a sessão e navega para /login com e-mail pré-preenchido (CA-01/CA-02/CA-03/CA-05)', async () => {
    const { httpMock, session, router } = await setup();
    const navigateSpy = vi.spyOn(router, 'navigate');
    await fillValidForm();

    await userEvent.click(screen.getByRole('button', { name: /alterar senha/i }));

    const req = httpMock.expectOne(CHANGE_PASSWORD_PATH);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      currentPassword: 'senhaAtual1',
      newPassword: 'novaSenha1',
    });
    req.flush(null, { status: 204, statusText: 'No Content' });

    expect(session.isAuthenticated()).toBe(false);
    expect(session.accessToken()).toBeNull();
    expect(session.lastEndReason()).toBe('password_changed');
    expect(navigateSpy).toHaveBeenCalledWith(['/login'], {
      queryParams: { email: 'ana@example.com' },
      replaceUrl: true,
    });
  });

  it('indicador de requisitos é o mesmo componente de FE-08 e atualiza ao vivo (CA-08)', async () => {
    await setup();

    await userEvent.type(screen.getByLabelText(/^nova senha$/i), 'novaSenha1');

    expect(screen.getAllByText('(atendido)', { exact: false }).length).toBe(3);
  });

  it('nova senha com 7 caracteres é rejeitada com a mensagem do critério que falta (CA-07)', async () => {
    await setup();

    await userEvent.type(screen.getByLabelText(/^nova senha$/i), 'abc1234');
    await userEvent.tab();

    expect(screen.getByText(/não atende aos requisitos/i)).toBeTruthy();
  });

  it('nova senha e confirmação divergentes impedem o envio (CA-09)', async () => {
    await setup();

    await userEvent.type(screen.getByLabelText(/senha atual/i), 'senhaAtual1');
    await userEvent.type(screen.getByLabelText(/^nova senha$/i), 'novaSenha1');
    await userEvent.type(screen.getByLabelText(/confirmar nova senha/i), 'outraSenha1');
    await userEvent.click(screen.getByRole('button', { name: /alterar senha/i }));

    expect(screen.getByText(/as senhas não coincidem/i)).toBeTruthy();
  });

  it('nova senha igual à atual é rejeitada com mensagem no campo da nova senha (CA-10)', async () => {
    await setup();

    await userEvent.type(screen.getByLabelText(/senha atual/i), 'senhaIgual1');
    await userEvent.type(screen.getByLabelText(/^nova senha$/i), 'senhaIgual1');
    await userEvent.type(screen.getByLabelText(/confirmar nova senha/i), 'senhaIgual1');
    await userEvent.click(screen.getByRole('button', { name: /alterar senha/i }));

    expect(screen.getByText(/diferente da atual/i)).toBeTruthy();
  });

  it('campos vazios impedem o envio (CA-11)', async () => {
    const { httpMock } = await setup();

    await userEvent.click(screen.getByRole('button', { name: /alterar senha/i }));

    httpMock.expectNone(CHANGE_PASSWORD_PATH);
  });

  it('senha atual incorreta (400) mostra erro no campo "senha atual" e não encerra a sessão (CA-12/CA-13)', async () => {
    const { httpMock, session } = await setup();
    await fillValidForm();

    await userEvent.click(screen.getByRole('button', { name: /alterar senha/i }));

    const req = httpMock.expectOne(CHANGE_PASSWORD_PATH);
    req.flush(
      {
        errorCode: 'auth.invalid_current_password',
        errors: { currentPassword: ['Senha atual incorreta.'] },
      },
      { status: 400, statusText: 'Bad Request' },
    );

    expect(await screen.findByText('Senha atual incorreta.')).toBeTruthy();
    expect(session.isAuthenticated()).toBe(true);

    // CA-13: corrigir e reenviar funciona sem recarregar.
    await userEvent.clear(screen.getByLabelText(/senha atual/i));
    await userEvent.type(screen.getByLabelText(/senha atual/i), 'senhaCorreta1');
    await userEvent.click(screen.getByRole('button', { name: /alterar senha/i }));
    httpMock.expectOne(CHANGE_PASSWORD_PATH).flush(null, { status: 204, statusText: 'No Content' });
    expect(session.isAuthenticated()).toBe(false);
  });

  it('erro 400 genérico é exibido no campo correspondente, não em toast genérico (CA-14)', async () => {
    const { httpMock } = await setup();
    await fillValidForm();

    await userEvent.click(screen.getByRole('button', { name: /alterar senha/i }));

    const req = httpMock.expectOne(CHANGE_PASSWORD_PATH);
    req.flush(
      { errors: { newPassword: ['Senha muito comum.'] } },
      { status: 400, statusText: 'Bad Request' },
    );

    expect(await screen.findByText('Senha muito comum.')).toBeTruthy();
  });

  it('erro de rede exibe mensagem de conectividade e a sessão permanece ativa (CA-15)', async () => {
    const { httpMock, session } = await setup();
    await fillValidForm();

    await userEvent.click(screen.getByRole('button', { name: /alterar senha/i }));

    const req = httpMock.expectOne(CHANGE_PASSWORD_PATH);
    req.flush({}, { status: 0, statusText: 'Unknown Error' });

    expect(await screen.findByRole('alert')).toBeTruthy();
    expect(session.isAuthenticated()).toBe(true);
  });

  it('nenhuma das três senhas aparece em storage (CA-16, segurança)', async () => {
    await setup();
    await fillValidForm();

    expect(JSON.stringify({ ...localStorage })).not.toContain('senhaAtual1');
    expect(JSON.stringify({ ...localStorage })).not.toContain('novaSenha1');
    expect(JSON.stringify({ ...sessionStorage })).not.toContain('senhaAtual1');
    expect(JSON.stringify({ ...sessionStorage })).not.toContain('novaSenha1');
  });

  it('usa autocomplete correto (CA-17)', async () => {
    await setup();

    expect(screen.getByLabelText(/senha atual/i)).toHaveAttribute(
      'autocomplete',
      'current-password',
    );
    expect(screen.getByLabelText(/^nova senha$/i)).toHaveAttribute('autocomplete', 'new-password');
  });

  it('botão de envio desabilita durante a requisição; clique duplo dispara uma chamada (CA-18)', async () => {
    const { httpMock } = await setup();
    await fillValidForm();

    const button = screen.getByRole('button', { name: /alterar senha/i });
    await userEvent.click(button);
    await userEvent.click(button);

    httpMock.expectOne(CHANGE_PASSWORD_PATH);
  });
});
