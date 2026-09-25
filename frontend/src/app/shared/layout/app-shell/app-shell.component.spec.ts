import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { render, screen } from '@testing-library/angular';

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
  return { ...utils, session };
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
