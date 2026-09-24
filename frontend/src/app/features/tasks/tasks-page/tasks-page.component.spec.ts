import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { render, screen } from '@testing-library/angular';

import { TasksPageComponent } from './tasks-page.component';
import { errorInterceptor } from '../../../core/errors/error.interceptor';
import { TaskResponse } from '../../../core/api/models/task.models';

function makeTask(overrides: Partial<TaskResponse> = {}): TaskResponse {
  return {
    id: '1',
    title: 'Estudar para a prova',
    description: null,
    priority: 'Medium',
    status: 'Pending',
    dueDate: '2026-01-01',
    completedAt: null,
    isOverdue: false,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    ...overrides,
  };
}

async function setup() {
  const utils = await render(TasksPageComponent, {
    providers: [
      provideHttpClient(withInterceptors([errorInterceptor])),
      provideHttpClientTesting(),
      provideRouter([{ path: 'tasks/new', children: [] }]),
    ],
  });
  const httpMock = utils.fixture.debugElement.injector.get(HttpTestingController);
  return { ...utils, httpMock };
}

describe('TasksPageComponent', () => {
  it('exibe o indicador de carregamento antes da resposta chegar', async () => {
    const { httpMock } = await setup();

    expect(screen.getByText(/carregando tarefas/i)).toBeTruthy();

    httpMock
      .expectOne((r) => r.url === '/api/tasks')
      .flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });
  });

  it('exibe o estado vazio quando não há tarefas', async () => {
    const { httpMock } = await setup();

    httpMock
      .expectOne((r) => r.url === '/api/tasks')
      .flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });

    expect(await screen.findByText(/você ainda não tem tarefas/i)).toBeTruthy();
    expect(screen.getByRole('button', { name: /criar a primeira tarefa/i })).toBeTruthy();
  });

  it('lista as tarefas retornadas, com título, prioridade, situação e vencimento', async () => {
    const { httpMock } = await setup();

    httpMock
      .expectOne((r) => r.url === '/api/tasks')
      .flush({
        items: [makeTask()],
        page: 1,
        pageSize: 20,
        totalCount: 1,
      });

    expect(await screen.findByText('Estudar para a prova')).toBeTruthy();
    expect(screen.getByText('Média')).toBeTruthy();
    expect(screen.getByText('Pendente')).toBeTruthy();
    expect(screen.getByText(/01\/01\/2026/)).toBeTruthy();
  });

  it('destaca a tarefa atrasada com o selo "Atrasada" (isOverdue vindo da API)', async () => {
    const { httpMock } = await setup();

    httpMock
      .expectOne((r) => r.url === '/api/tasks')
      .flush({
        items: [makeTask({ isOverdue: true })],
        page: 1,
        pageSize: 20,
        totalCount: 1,
      });

    expect(await screen.findByText('Atrasada')).toBeTruthy();
  });

  it('não exibe o selo "Atrasada" quando isOverdue é falso', async () => {
    const { httpMock } = await setup();

    httpMock
      .expectOne((r) => r.url === '/api/tasks')
      .flush({
        items: [makeTask({ isOverdue: false })],
        page: 1,
        pageSize: 20,
        totalCount: 1,
      });

    await screen.findByText('Estudar para a prova');
    expect(screen.queryByText('Atrasada')).toBeNull();
  });

  it('destaca o vencimento em atraso com o ponto e a cor de perigo junto à data', async () => {
    const { httpMock, container } = await setup();

    httpMock
      .expectOne((r) => r.url === '/api/tasks')
      .flush({
        items: [makeTask({ isOverdue: true })],
        page: 1,
        pageSize: 20,
        totalCount: 1,
      });

    await screen.findByText('Atrasada');

    expect(container.querySelector('.task-item__due-group--overdue')).toBeTruthy();
    expect(container.querySelector('.task-item__due-dot')).toBeTruthy();
    expect(container.querySelector('.task-item__meta-item--overdue')).toBeNull();
  });

  it('não marca o vencimento como atrasado quando a tarefa está em dia', async () => {
    const { httpMock, container } = await setup();

    httpMock
      .expectOne((r) => r.url === '/api/tasks')
      .flush({
        items: [makeTask({ isOverdue: false })],
        page: 1,
        pageSize: 20,
        totalCount: 1,
      });

    await screen.findByText('Estudar para a prova');

    expect(container.querySelector('.task-item__due-group--overdue')).toBeNull();
    expect(container.querySelector('.task-item__due-dot')).toBeNull();
  });

  it('exibe erro com "tentar novamente" e refaz a chamada ao clicar', async () => {
    const { httpMock } = await setup();

    httpMock
      .expectOne((r) => r.url === '/api/tasks')
      .flush({ errorCode: 'unknown' }, { status: 500, statusText: 'Server Error' });

    const retryButton = await screen.findByRole('button', { name: /tentar novamente/i });
    retryButton.click();

    httpMock
      .expectOne((r) => r.url === '/api/tasks')
      .flush({ items: [makeTask()], page: 1, pageSize: 20, totalCount: 1 });

    expect(await screen.findByText('Estudar para a prova')).toBeTruthy();
  });

  it('paginação: "anterior" fica desabilitado na primeira página e "próxima" habilitado quando há mais páginas', async () => {
    const { httpMock } = await setup();

    httpMock
      .expectOne((r) => r.url === '/api/tasks')
      .flush({
        items: [makeTask()],
        page: 1,
        pageSize: 20,
        totalCount: 40,
      });

    expect(await screen.findByText(/página 1 de 2/i)).toBeTruthy();
    expect(screen.getByRole('button', { name: /anterior/i })).toBeDisabled();
    expect(screen.getByRole('button', { name: /próxima/i })).not.toBeDisabled();
  });

  it('clicar em "próxima" carrega a página seguinte', async () => {
    const { httpMock } = await setup();

    httpMock
      .expectOne((r) => r.url === '/api/tasks')
      .flush({
        items: [makeTask({ id: '1', title: 'Página 1' })],
        page: 1,
        pageSize: 20,
        totalCount: 40,
      });

    await screen.findByText('Página 1');
    screen.getByRole('button', { name: /próxima/i }).click();

    const req = httpMock.expectOne((r) => r.url === '/api/tasks');
    expect(req.request.params.get('page')).toBe('2');
    req.flush({
      items: [makeTask({ id: '2', title: 'Página 2' })],
      page: 2,
      pageSize: 20,
      totalCount: 40,
    });

    expect(await screen.findByText('Página 2')).toBeTruthy();
  });

  it('não exibe controles de paginação com uma única página', async () => {
    const { httpMock } = await setup();

    httpMock
      .expectOne((r) => r.url === '/api/tasks')
      .flush({
        items: [makeTask()],
        page: 1,
        pageSize: 20,
        totalCount: 1,
      });

    await screen.findByText('Estudar para a prova');
    expect(screen.queryByRole('button', { name: /anterior/i })).toBeNull();
    expect(screen.queryByRole('button', { name: /próxima/i })).toBeNull();
  });
});
