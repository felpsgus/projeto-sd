import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Route, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { routes } from './app.routes';
import { authGuard } from './core/auth/auth.guard';
import { unsavedChangesGuard } from './features/tasks/edit-task/unsaved-changes.guard';
import { SessionStore } from './core/auth/session-store';

/** Rotas-folha (as que renderizam uma página) com o caminho completo e se herdam `authGuard`. */
function leaves(
  list: Routes,
  prefix = '',
  guarded = false,
): { path: string; route: Route; guarded: boolean }[] {
  return list.flatMap((route) => {
    const isGuarded = guarded || !!route.canActivate?.includes(authGuard);
    const path = [prefix, route.path].filter(Boolean).join('/');
    if (route.children) {
      return leaves(route.children, path, isGuarded);
    }
    return route.redirectTo ? [] : [{ path, route, guarded: isGuarded }];
  });
}
type Routes = readonly Route[];

describe('app.routes', () => {
  // FE-07, CA-05: uma rota nova esquecida sem authGuard quebra o build.
  it('toda rota fora de /login, /register e ** exige authGuard (FE-07 CA-05)', () => {
    const open = ['login', 'register', '**'];
    const unguarded = leaves(routes)
      .filter((l) => !open.includes(l.path) && !l.guarded)
      .map((l) => l.path);

    expect(unguarded).toEqual([]);
    expect(leaves(routes).length).toBeGreaterThanOrEqual(8);
  });

  // FE-04, CA-10
  it('toda página declara um title não vazio e distinto (FE-04 CA-10)', () => {
    const titles = leaves(routes).map((l) => l.route.title);

    titles.forEach((t) => expect(typeof t === 'string' && t.trim().length > 0).toBe(true));
    expect(new Set(titles).size).toBe(titles.length);
  });
});

describe('app.routes — saída de formulários', () => {
  // FE-18, CA-23: criar e editar avisam antes de descartar alterações.
  it.each(['new', ':id/edit'])('tasks/%s declara a unsavedChangesGuard', (path) => {
    const child = routes.find((r) => r.path === 'tasks')?.children?.find((r) => r.path === path);

    expect(child?.canDeactivate).toContain(unsavedChangesGuard);
  });
});

describe('páginas renderizadas pelas rotas', () => {
  async function navigate(url: string, authenticated: boolean) {
    TestBed.configureTestingModule({
      providers: [provideRouter(routes), provideHttpClient(), provideHttpClientTesting()],
    });
    const session = TestBed.inject(SessionStore);
    if (authenticated) {
      session.startSession({ accessToken: 't', expiresAt: new Date().toISOString() }, 'a@b.com');
    } else {
      session.finishBootstrap();
    }
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url);
    harness.detectChanges();
    return harness.fixture.nativeElement as HTMLElement;
  }

  const pages: [string, boolean, string][] = [
    ['/login', false, 'Entrar — TodoList'],
    ['/register', false, 'Cadastro — TodoList'],
    ['/tasks', true, 'Tarefas — TodoList'],
    ['/tasks/new', true, 'Nova tarefa — TodoList'],
    ['/tasks/abc/edit', true, 'Editar tarefa — TodoList'],
    ['/account', true, 'Minha conta — TodoList'],
    ['/account/password', true, 'Alterar senha — TodoList'],
    ['/nao/existe', true, 'Página não encontrada — TodoList'],
  ];

  // FE-04, CA-08 e CA-10
  it.each(pages)(
    '%s tem um único <h1>, um único <main> e muda o <title> (FE-04 CA-08/CA-10)',
    async (url, authenticated, title) => {
      const root = await navigate(url, authenticated);

      expect(root.querySelectorAll('h1').length).toBe(1);
      expect(root.querySelectorAll('main').length).toBe(1);
      expect(document.title).toBe(title);
    },
  );

  // FE-07, CA-10: um returnUrl interno inexistente (navegado após o login) cai na 404 da aplicação.
  it('rota interna inexistente mostra a página 404 (FE-07 CA-10)', async () => {
    const root = await navigate('/nao/existe', true);

    expect(root.querySelector('h1')?.textContent).toBe('Página não encontrada');
  });
});
