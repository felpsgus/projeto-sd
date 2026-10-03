import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { render, screen } from '@testing-library/angular';
import userEvent from '@testing-library/user-event';

import { AppShellComponent } from './app-shell.component';
import { SessionStore } from '../../../core/auth/session-store';

async function setup() {
  const utils = await render(AppShellComponent, {
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideRouter([{ path: 'login', children: [] }]),
    ],
  });
  const session = utils.fixture.debugElement.injector.get(SessionStore);
  const httpMock = utils.fixture.debugElement.injector.get(HttpTestingController);
  return { ...utils, session, httpMock };
}

describe('AppShellComponent (FE-11, CA-07)', () => {
  it('mostra o e-mail quando o nome de exibição ainda não é conhecido', async () => {
    const { session, fixture } = await setup();
    session.startSession(
      { accessToken: 'tok', expiresAt: new Date().toISOString() },
      'ana@example.com',
    );
    fixture.detectChanges();

    expect(screen.getByRole('link', { name: 'ana@example.com' })).toBeTruthy();
  });

  it('mostra o nome de exibição assim que setDisplayName é chamado — sem recarregar', async () => {
    const { session, fixture } = await setup();
    session.startSession(
      { accessToken: 'tok', expiresAt: new Date().toISOString() },
      'ana@example.com',
    );
    fixture.detectChanges();
    expect(screen.getByRole('link', { name: 'ana@example.com' })).toBeTruthy();

    session.setDisplayName('Ana Paula');
    fixture.detectChanges();

    expect(screen.getByRole('link', { name: 'Ana Paula' })).toBeTruthy();
    expect(screen.queryByText('ana@example.com')).toBeNull();
  });

  it('o link do nome/e-mail aponta para /account', async () => {
    const { session, fixture } = await setup();
    session.startSession(
      { accessToken: 'tok', expiresAt: new Date().toISOString() },
      'ana@example.com',
    );
    fixture.detectChanges();

    expect(screen.getByRole('link', { name: 'ana@example.com' })).toHaveAttribute(
      'href',
      '/account',
    );
  });
});

describe('AppShellComponent — Sair (FE-10)', () => {
  it('"Sair" chama POST /api/auth/logout, encerra a sessão e não pede confirmação (CA-01/CA-02/CA-12)', async () => {
    const { session, fixture, httpMock } = await setup();
    session.startSession({ accessToken: 'tok', expiresAt: new Date().toISOString() }, 'a@b.com');
    fixture.detectChanges();

    await userEvent.click(screen.getByRole('button', { name: 'Sair' }));

    expect(screen.queryByRole('dialog')).toBeNull();
    const req = httpMock.expectOne('/api/auth/logout');
    req.flush(null, { status: 204, statusText: 'No Content' });
    expect(session.isAuthenticated()).toBe(false);
    expect(session.lastEndReason()).toBe('user_logout');
  });
});
