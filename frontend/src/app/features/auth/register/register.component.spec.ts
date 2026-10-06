import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { render, screen } from '@testing-library/angular';
import userEvent from '@testing-library/user-event';

import { RegisterComponent } from './register.component';
import { REGISTER_PATH } from '../../../core/api/auth-api.service';
import { errorInterceptor } from '../../../core/errors/error.interceptor';
import { ERROR_MESSAGES } from '../../../core/errors/error-messages';

async function setup() {
  const utils = await render(RegisterComponent, {
    providers: [
      provideHttpClient(withInterceptors([errorInterceptor])),
      provideHttpClientTesting(),
      provideRouter([{ path: 'login', children: [] }]),
    ],
  });
  const httpMock = utils.fixture.debugElement.injector.get(HttpTestingController);
  const router = utils.fixture.debugElement.injector.get(Router);
  return { ...utils, httpMock, router };
}

async function fillValidForm() {
  await userEvent.type(screen.getByLabelText(/^e-mail$/i), 'nova@example.com');
  await userEvent.type(screen.getByLabelText(/^senha$/i), 'abcdef12');
  await userEvent.type(screen.getByLabelText(/confirmar senha/i), 'abcdef12');
}

describe('RegisterComponent (FE-08)', () => {
  it('botão de envio começa desabilitado (CA-02)', async () => {
    await setup();

    expect(screen.getByRole('button', { name: /criar conta/i })).toBeDisabled();
  });

  it('cadastro válido chama a API e navega para /login com e-mail pré-preenchido (CA-01)', async () => {
    const { httpMock, router } = await setup();
    const navigateSpy = vi.spyOn(router, 'navigate');

    await fillValidForm();
    await userEvent.click(screen.getByRole('button', { name: /criar conta/i }));

    const req = httpMock.expectOne(REGISTER_PATH);
    expect(req.request.method).toBe('POST');
    req.flush({
      id: '1',
      email: 'nova@example.com',
      displayName: 'nova',
      createdAt: '2026-01-01T00:00:00Z',
    });

    expect(navigateSpy).toHaveBeenCalledWith(['/login'], {
      queryParams: { registered: '1', email: 'nova@example.com' },
    });
  });

  it('clique duplo dispara uma única requisição (CA-03)', async () => {
    const { httpMock } = await setup();
    await fillValidForm();

    const button = screen.getByRole('button', { name: /criar conta/i });
    await userEvent.click(button);
    await userEvent.click(button);

    httpMock.expectOne(REGISTER_PATH);
  });

  it('e-mail inválido mostra erro junto ao campo antes do envio (CA-04)', async () => {
    await setup();

    const email = screen.getByLabelText(/^e-mail$/i);
    await userEvent.type(email, 'invalido');
    await userEvent.tab();

    expect(screen.getByText(/informe um e-mail válido/i)).toBeTruthy();
    expect(email).toHaveAttribute('aria-invalid', 'true');
  });

  it('mensagens de erro só aparecem após o campo ser tocado (CA-10)', async () => {
    await setup();

    expect(screen.queryByText(/informe um e-mail válido/i)).toBeNull();
    expect(screen.queryByText(/informe seu e-mail/i)).toBeNull();
  });

  it('indicador de requisitos de senha atualiza ao vivo (CA-06)', async () => {
    await setup();

    await userEvent.type(screen.getByLabelText(/^senha$/i), 'abcdef12');

    expect(screen.getAllByText('(atendido)', { exact: false }).length).toBe(3);
  });

  it('senha e confirmação diferentes impedem o envio (CA-07)', async () => {
    await setup();

    await userEvent.type(screen.getByLabelText(/^e-mail$/i), 'nova@example.com');
    await userEvent.type(screen.getByLabelText(/^senha$/i), 'abcdef12');
    await userEvent.type(screen.getByLabelText(/confirmar senha/i), 'outraSenha1');
    await userEvent.tab();

    expect(screen.getByText(/as senhas não coincidem/i)).toBeTruthy();
    expect(screen.getByRole('button', { name: /criar conta/i })).toBeDisabled();
  });

  it('nome de exibição com 101 caracteres é rejeitado; com 100 é aceito (CA-08)', async () => {
    await setup();
    const displayName = screen.getByLabelText(/nome de exibição/i);

    await userEvent.type(displayName, 'a'.repeat(101));
    await userEvent.tab();
    expect(screen.getByText(/no máximo 100 caracteres/i)).toBeTruthy();

    await userEvent.clear(displayName);
    await userEvent.type(displayName, 'a'.repeat(100));
    await userEvent.tab();
    expect(screen.queryByText(/no máximo 100 caracteres/i)).toBeNull();
  });

  it('avisa que o nome vazio usa a parte antes do @ (CA-09)', async () => {
    await setup();

    expect(screen.getByText(/usaremos a parte do seu e-mail antes do/i)).toBeTruthy();
  });

  it('409 exibe mensagem junto ao campo de e-mail com link para o login (CA-11)', async () => {
    const { httpMock } = await setup();
    await fillValidForm();

    await userEvent.click(screen.getByRole('button', { name: /criar conta/i }));

    const req = httpMock.expectOne(REGISTER_PATH);
    req.flush(
      { errorCode: 'auth.email_already_registered' },
      { status: 409, statusText: 'Conflict' },
    );

    expect(await screen.findByText(/já está cadastrado/i)).toBeTruthy();
    expect(screen.getByRole('link', { name: /entrar com essa conta/i })).toHaveAttribute(
      'href',
      '/login',
    );
  });

  it('400 do backend exibe fieldErrors nos campos correspondentes (CA-12)', async () => {
    const { httpMock } = await setup();
    await fillValidForm();

    await userEvent.click(screen.getByRole('button', { name: /criar conta/i }));

    const req = httpMock.expectOne(REGISTER_PATH);
    req.flush(
      { errors: { email: ['E-mail inválido para o servidor.'] } },
      { status: 400, statusText: 'Bad Request' },
    );

    expect(await screen.findByText('E-mail inválido para o servidor.')).toBeTruthy();
  });

  it('senha usa autocomplete="new-password" e é do tipo password (CA-16)', async () => {
    await setup();

    expect(screen.getByLabelText(/^senha$/i)).toHaveAttribute('autocomplete', 'new-password');
    expect(screen.getByLabelText(/^senha$/i)).toHaveAttribute('type', 'password');
  });

  it('botão mostrar/ocultar senha alterna o tipo do campo e o rótulo acessível (CA-17)', async () => {
    await setup();

    const [toggle] = screen.getAllByRole('button', { name: /mostrar senha/i });
    if (!toggle) {
      throw new Error('toggle button not found');
    }
    await userEvent.click(toggle);

    expect(screen.getByLabelText(/^senha$/i)).toHaveAttribute('type', 'text');
    expect(screen.getAllByRole('button', { name: /ocultar senha/i }).length).toBeGreaterThan(0);
  });

  it('nenhuma senha aparece em localStorage/sessionStorage após digitar (CA-15, segurança)', async () => {
    await setup();

    await userEvent.type(screen.getByLabelText(/^senha$/i), 'segredo-super-secreto1');

    expect(JSON.stringify({ ...localStorage })).not.toContain('segredo-super-secreto1');
    expect(JSON.stringify({ ...sessionStorage })).not.toContain('segredo-super-secreto1');
  });

  // FE-08, CA-13 e CA-14
  it('erro de rede mostra a mensagem de conectividade, preserva os dados e permite reenviar (CA-13, CA-14)', async () => {
    const { httpMock } = await setup();
    await userEvent.type(screen.getByLabelText(/nome de exibição/i), 'Nova Pessoa');
    await fillValidForm();

    await userEvent.click(screen.getByRole('button', { name: /criar conta/i }));
    httpMock.expectOne(REGISTER_PATH).error(new ProgressEvent('error'));

    expect((await screen.findByRole('alert')).textContent).toContain(ERROR_MESSAGES['network']);
    expect(screen.getByLabelText(/^e-mail$/i)).toHaveValue('nova@example.com');
    expect(screen.getByLabelText(/nome de exibição/i)).toHaveValue('Nova Pessoa');
    // O código não limpa as senhas (o critério só dispensa preservá-las).
    expect(screen.getByLabelText(/^senha$/i)).toHaveValue('abcdef12');

    await userEvent.click(screen.getByRole('button', { name: /criar conta/i }));
    httpMock.expectOne(REGISTER_PATH);
  });

  // FE-08, CA-18 (e FE-04, CA-13): label associado, mensagem ligada por aria-describedby, aria-invalid.
  it('liga a mensagem de erro ao campo por aria-describedby e marca aria-invalid (CA-18)', async () => {
    await setup();
    const email = screen.getByLabelText(/^e-mail$/i);
    expect(email).toHaveAttribute('aria-invalid', 'false');
    expect(email).not.toHaveAttribute('aria-describedby');

    await userEvent.type(email, 'invalido');
    await userEvent.tab();

    expect(email).toHaveAttribute('aria-invalid', 'true');
    const describedBy = email.getAttribute('aria-describedby');
    expect(describedBy).toBeTruthy();
    expect(document.getElementById(describedBy ?? '')?.textContent).toContain(
      'Informe um e-mail válido.',
    );
  });

  // FE-08, CA-20
  it('após falha do servidor, o foco vai para o primeiro campo com erro (CA-20)', async () => {
    const { httpMock } = await setup();
    await fillValidForm();

    await userEvent.click(screen.getByRole('button', { name: /criar conta/i }));
    httpMock
      .expectOne(REGISTER_PATH)
      .flush(
        { errors: { password: ['Senha fraca.'], displayName: ['Nome inválido.'] } },
        { status: 400, statusText: 'Bad Request' },
      );

    await vi.waitFor(() => expect(screen.getByLabelText(/^senha$/i)).toHaveFocus());
  });
});
