import { Routes } from '@angular/router';

import { authGuard } from './core/auth/auth.guard';
import { guestGuard } from './core/auth/guest.guard';
import { unsavedChangesGuard } from './features/tasks/edit-task/unsaved-changes.guard';

/**
 * Mapa de rotas (FE-07): `/login` pública, `/tasks`, `/tasks/new` e `/tasks/:id/edit`
 * (FE-18) autenticadas (protegidas pelo `authGuard` do pai), redirect da raiz e 404. Sem
 * `/register` nem `/account*` — ainda fora do recorte.
 *
 * Lazy loading por feature via `loadComponent`: o bundle inicial não carrega o código de
 * `auth` nem de `tasks` (FE-07, CA-11).
 */
export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'tasks' },
  {
    path: 'login',
    canActivate: [guestGuard],
    loadComponent: () =>
      import('./shared/layout/auth-layout/auth-layout.component').then(
        (m) => m.AuthLayoutComponent,
      ),
    children: [
      {
        path: '',
        loadComponent: () =>
          import('./features/auth/login/login.component').then((m) => m.LoginComponent),
        title: 'Entrar — TodoList',
      },
    ],
  },
  {
    path: 'tasks',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./shared/layout/app-shell/app-shell.component').then((m) => m.AppShellComponent),
    children: [
      {
        path: '',
        loadComponent: () =>
          import('./features/tasks/tasks-page/tasks-page.component').then(
            (m) => m.TasksPageComponent,
          ),
        title: 'Tarefas — TodoList',
      },
      {
        path: 'new',
        loadComponent: () =>
          import('./features/tasks/create-task/create-task.component').then(
            (m) => m.CreateTaskComponent,
          ),
        title: 'Nova tarefa — TodoList',
      },
      {
        path: ':id/edit',
        canDeactivate: [unsavedChangesGuard],
        loadComponent: () =>
          import('./features/tasks/edit-task/edit-task.component').then((m) => m.EditTaskComponent),
        title: 'Editar tarefa — TodoList',
      },
    ],
  },
  {
    path: '**',
    loadComponent: () =>
      import('./features/not-found/not-found.component').then((m) => m.NotFoundComponent),
    title: 'Página não encontrada — TodoList',
  },
];
