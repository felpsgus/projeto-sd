import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Routes } from '@angular/router';
import { render, screen, waitFor, within } from '@testing-library/angular';
import userEvent from '@testing-library/user-event';

import { TasksPageComponent } from './tasks-page.component';
import { errorInterceptor } from '../../../core/errors/error.interceptor';
import { TaskResponse } from '../../../core/api/models/task.models';
import { SessionStore } from '../../../core/auth/session-store';

const ROUTES: Routes = [
  { path: 'tasks', children: [] },
  { path: 'tasks/new', children: [] },
  { path: 'tasks/:id/edit', children: [] },
];

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

async function setup(initialRoute = 'tasks') {
  const utils = await render(TasksPageComponent, {
    routes: ROUTES,
    initialRoute,
    providers: [
      provideHttpClient(withInterceptors([errorInterceptor])),
      provideHttpClientTesting(),
      // Sessão ativa: sem ela o TasksStore limpa o estado (effect em isAuthenticated) logo
      // depois do primeiro load.
      {
        provide: SessionStore,
        useFactory: () => {
          const session = new SessionStore();
          session.startSession(
            { accessToken: 'tok', expiresAt: '2099-01-01T00:00:00Z' },
            'a@b.com',
          );
          return session;
        },
      },
    ],
  });
  const httpMock = utils.fixture.debugElement.injector.get(HttpTestingController);
  return { ...utils, httpMock };
}

/** A primeira `GET /api/tasks` disparada ao montar o componente (sem filtro nenhum, salvo
 * quando `initialRoute` já traz query params). */
function firstListRequest(httpMock: HttpTestingController) {
  return httpMock.expectOne((r) => r.url === '/api/tasks' && r.method === 'GET');
}

describe('TasksPageComponent', () => {
  it('exibe o indicador de carregamento antes da resposta chegar', async () => {
    const { httpMock } = await setup();

    expect(screen.getByText(/carregando tarefas/i)).toBeTruthy();

    firstListRequest(httpMock).flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });
  });

  it('exibe o estado vazio quando não há tarefas e nenhum filtro ativo', async () => {
    const { httpMock } = await setup();

    firstListRequest(httpMock).flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });

    expect(await screen.findByText(/você ainda não tem tarefas/i)).toBeTruthy();
    expect(screen.getByRole('button', { name: /criar a primeira tarefa/i })).toBeTruthy();
  });

  it('lista as tarefas retornadas, com título, prioridade, situação e vencimento', async () => {
    const { httpMock } = await setup();

    firstListRequest(httpMock).flush({
      items: [makeTask()],
      page: 1,
      pageSize: 20,
      totalCount: 1,
    });

    expect(await screen.findByText('Estudar para a prova')).toBeTruthy();
    // Escopado à lista: "Média"/"Pendente" também poderiam casar com rótulos da barra de
    // filtros (o texto "Média" é compartilhado pelo filtro de prioridade e pelo item).
    const list = within(screen.getByRole('list'));
    expect(list.getByText('Média')).toBeTruthy();
    expect(list.getByText('Pendente')).toBeTruthy();
    expect(screen.getByText(/01\/01\/2026/)).toBeTruthy();
  });

  it('exibe erro com "tentar novamente" e refaz a chamada ao clicar', async () => {
    const { httpMock } = await setup();

    firstListRequest(httpMock).flush(
      { errorCode: 'unknown' },
      { status: 500, statusText: 'Server Error' },
    );

    const retryButton = await screen.findByRole('button', { name: /tentar novamente/i });
    await userEvent.click(retryButton);

    firstListRequest(httpMock).flush({ items: [makeTask()], page: 1, pageSize: 20, totalCount: 1 });

    expect(await screen.findByText('Estudar para a prova')).toBeTruthy();
  });

  describe('filtros (FE-16)', () => {
    it('filtrar por "Pendentes" mostra só pendentes (CA-01)', async () => {
      const { httpMock } = await setup();

      firstListRequest(httpMock).flush({
        items: [
          makeTask({ id: '1' }),
          makeTask({ id: '2', title: 'Tarefa concluída', status: 'Completed' }),
        ],
        page: 1,
        pageSize: 20,
        totalCount: 2,
      });
      await screen.findByText('Estudar para a prova');
      await screen.findByText('Tarefa concluída');

      await userEvent.click(screen.getByRole('radio', { name: 'Pendentes' }));

      const req = await waitFor(() =>
        httpMock.expectOne((r) => r.url === '/api/tasks' && r.params.get('status') === 'pending'),
      );
      req.flush({
        items: [makeTask({ id: '1' })],
        page: 1,
        pageSize: 20,
        totalCount: 1,
      });

      expect(await screen.findByText('Estudar para a prova')).toBeTruthy();
      expect(screen.queryByText('Tarefa concluída')).toBeNull();
    });

    it('filtrar por prioridade Alta envia priority=high (CA-02)', async () => {
      const { httpMock } = await setup();

      firstListRequest(httpMock).flush({
        items: [makeTask()],
        page: 1,
        pageSize: 20,
        totalCount: 1,
      });
      await screen.findByText('Estudar para a prova');

      await userEvent.click(screen.getByRole('checkbox', { name: 'Alta' }));

      const req = await waitFor(() =>
        httpMock.expectOne(
          (r) => r.url === '/api/tasks' && (r.params.getAll('priority') ?? []).length > 0,
        ),
      );
      expect(req.request.params.getAll('priority')).toEqual(['high']);
      req.flush({
        items: [makeTask({ priority: 'High' })],
        page: 1,
        pageSize: 20,
        totalCount: 1,
      });

      expect(await screen.findByText('Alta')).toBeTruthy();
    });

    it('selecionar Baixa e Alta envia priority=low&priority=high (CA-03)', async () => {
      const { httpMock } = await setup();

      firstListRequest(httpMock).flush({
        items: [makeTask()],
        page: 1,
        pageSize: 20,
        totalCount: 1,
      });
      await screen.findByText('Estudar para a prova');

      await userEvent.click(screen.getByRole('checkbox', { name: 'Baixa' }));
      const firstReq = await waitFor(() =>
        httpMock.expectOne(
          (r) => r.url === '/api/tasks' && (r.params.getAll('priority') ?? []).length > 0,
        ),
      );
      firstReq.flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });

      await userEvent.click(screen.getByRole('checkbox', { name: 'Alta' }));
      const secondReq = await waitFor(() =>
        httpMock.expectOne(
          (r) => r.url === '/api/tasks' && (r.params.getAll('priority') ?? []).length === 2,
        ),
      );
      expect(secondReq.request.params.getAll('priority')).toEqual(['low', 'high']);
      secondReq.flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });
    });

    it('alternador "Atrasadas" envia overdue=true (CA-04)', async () => {
      const { httpMock } = await setup();

      firstListRequest(httpMock).flush({
        items: [makeTask()],
        page: 1,
        pageSize: 20,
        totalCount: 1,
      });
      await screen.findByText('Estudar para a prova');

      await userEvent.click(screen.getByRole('checkbox', { name: /somente atrasadas/i }));

      const req = await waitFor(() =>
        httpMock.expectOne((r) => r.url === '/api/tasks' && r.params.get('overdue') === 'true'),
      );
      req.flush({
        items: [makeTask({ isOverdue: true })],
        page: 1,
        pageSize: 20,
        totalCount: 1,
      });

      expect(await screen.findByText('Atrasada')).toBeTruthy();
    });

    it('filtros combinam: estado + prioridade + atrasadas + busca (CA-05)', async () => {
      const { httpMock } = await setup(
        'tasks?status=pending&priority=high&overdue=true&search=prova',
      );

      const req = firstListRequest(httpMock);
      expect(req.request.params.get('status')).toBe('pending');
      expect(req.request.params.getAll('priority')).toEqual(['high']);
      expect(req.request.params.get('overdue')).toBe('true');
      expect(req.request.params.get('search')).toBe('prova');
      req.flush({ items: [makeTask()], page: 1, pageSize: 20, totalCount: 1 });

      expect(await screen.findByText('Estudar para a prova')).toBeTruthy();
    });

    it('"limpar filtros" volta ao estado inicial e some quando não há filtro ativo (CA-06/CA-07)', async () => {
      const { httpMock } = await setup('tasks?status=pending');

      firstListRequest(httpMock).flush({
        items: [makeTask()],
        page: 1,
        pageSize: 20,
        totalCount: 1,
      });
      await screen.findByText('Estudar para a prova');

      const clearButton = screen.getByRole('button', { name: /limpar filtros/i });
      await userEvent.click(clearButton);

      const req = await waitFor(() =>
        httpMock.expectOne((r) => r.url === '/api/tasks' && !r.params.has('status')),
      );
      req.flush({ items: [makeTask()], page: 1, pageSize: 20, totalCount: 1 });

      await waitFor(() =>
        expect(screen.queryByRole('button', { name: /limpar filtros/i })).toBeNull(),
      );
    });

    it('buscar por termo no título retorna a tarefa (CA-08)', async () => {
      const { httpMock } = await setup();

      firstListRequest(httpMock).flush({
        items: [makeTask()],
        page: 1,
        pageSize: 20,
        totalCount: 1,
      });
      await screen.findByText('Estudar para a prova');

      await userEvent.type(screen.getByLabelText(/buscar/i), 'prova');

      const req = await waitFor(
        () =>
          httpMock.expectOne((r) => r.url === '/api/tasks' && r.params.get('search') === 'prova'),
        { timeout: 2000 },
      );
      req.flush({ items: [makeTask()], page: 1, pageSize: 20, totalCount: 1 });

      expect(await screen.findByText('Estudar para a prova')).toBeTruthy();
    });

    it('buscar por termo presente na descrição também encontra a tarefa (CA-09)', async () => {
      const { httpMock } = await setup();

      firstListRequest(httpMock).flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });
      await screen.findByText(/você ainda não tem tarefas/i);

      await userEvent.type(screen.getByLabelText(/buscar/i), 'capítulo');

      const req = await waitFor(
        () =>
          httpMock.expectOne(
            (r) => r.url === '/api/tasks' && r.params.get('search') === 'capítulo',
          ),
        { timeout: 2000 },
      );
      req.flush({
        items: [makeTask({ description: 'Capítulo 1 a 3' })],
        page: 1,
        pageSize: 20,
        totalCount: 1,
      });

      expect(await screen.findByText('Estudar para a prova')).toBeTruthy();
    });

    it('digitar rapidamente dispara uma única requisição, não uma por tecla (CA-10)', async () => {
      const { httpMock } = await setup();

      firstListRequest(httpMock).flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });
      await screen.findByText(/você ainda não tem tarefas/i);

      await userEvent.type(screen.getByLabelText(/buscar/i), 'relatorio');

      const req = await waitFor(
        () =>
          httpMock.expectOne(
            (r) => r.url === '/api/tasks' && r.params.get('search') === 'relatorio',
          ),
        { timeout: 2000 },
      );
      req.flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });

      // Nenhuma outra requisição de listagem foi feita (uma por tecla teria gerado 9).
      httpMock.expectNone((r) => r.url === '/api/tasks');
    });

    it('resultado fora de ordem não deixa a lista com a resposta antiga (CA-11)', async () => {
      const { httpMock } = await setup();

      firstListRequest(httpMock).flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });
      await screen.findByText(/você ainda não tem tarefas/i);

      await userEvent.click(screen.getByRole('radio', { name: 'Pendentes' }));
      const firstReq = await waitFor(() =>
        httpMock.expectOne((r) => r.url === '/api/tasks' && r.params.get('status') === 'pending'),
      );

      await userEvent.click(screen.getByRole('radio', { name: 'Concluídas' }));
      const secondReq = await waitFor(() =>
        httpMock.expectOne((r) => r.url === '/api/tasks' && r.params.get('status') === 'completed'),
      );

      // A resposta da segunda chamada chega primeiro; a da primeira, depois — fora de ordem.
      secondReq.flush({
        items: [makeTask({ id: '2', title: 'Tarefa concluída', status: 'Completed' })],
        page: 1,
        pageSize: 20,
        totalCount: 1,
      });
      firstReq.flush({
        items: [makeTask({ id: '1', title: 'Tarefa pendente' })],
        page: 1,
        pageSize: 20,
        totalCount: 1,
      });

      expect(await screen.findByText('Tarefa concluída')).toBeTruthy();
      expect(screen.queryByText('Tarefa pendente')).toBeNull();
    });

    it('limpar a busca remove "search" da URL e recarrega a lista completa (CA-12)', async () => {
      const { httpMock } = await setup('tasks?search=prova');

      firstListRequest(httpMock).flush({
        items: [makeTask()],
        page: 1,
        pageSize: 20,
        totalCount: 1,
      });
      await screen.findByText('Estudar para a prova');

      const searchBox = screen.getByLabelText(/buscar/i) as HTMLInputElement;
      await userEvent.clear(searchBox);

      const req = await waitFor(
        () => httpMock.expectOne((r) => r.url === '/api/tasks' && !r.params.has('search')),
        { timeout: 2000 },
      );
      req.flush({ items: [makeTask()], page: 1, pageSize: 20, totalCount: 1 });
    });

    it('busca sem resultado exibe o estado vazio de filtro, distinto do inicial (CA-13)', async () => {
      const { httpMock } = await setup('tasks?search=inexistente');

      firstListRequest(httpMock).flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });

      expect(await screen.findByText(/nenhuma tarefa encontrada com esses filtros/i)).toBeTruthy();
      // Duas ações "Limpar filtros" coexistem de propósito: a da barra de filtros (sempre
      // visível com filtro ativo) e a do estado vazio (CA-13) — daqui, `getAllByRole`.
      expect(screen.getAllByRole('button', { name: /limpar filtros/i }).length).toBeGreaterThan(0);
      expect(screen.queryByText(/você ainda não tem tarefas/i)).toBeNull();
    });
  });

  describe('URL (FE-16)', () => {
    it('abrir a URL com filtros já aplica os dois e a lista vem filtrada (CA-15)', async () => {
      const { httpMock } = await setup('tasks?status=pending&priority=high');

      const req = firstListRequest(httpMock);
      expect(req.request.params.get('status')).toBe('pending');
      expect(req.request.params.getAll('priority')).toEqual(['high']);
      req.flush({ items: [makeTask()], page: 1, pageSize: 20, totalCount: 1 });

      expect(await screen.findByText('Estudar para a prova')).toBeTruthy();
    });

    it('recarregar a página preserva filtros, busca e página (CA-16)', async () => {
      const { httpMock } = await setup('tasks?status=completed&search=prova&page=2');

      const req = firstListRequest(httpMock);
      expect(req.request.params.get('status')).toBe('completed');
      expect(req.request.params.get('search')).toBe('prova');
      expect(req.request.params.get('page')).toBe('2');
      req.flush({
        items: [makeTask({ status: 'Completed' })],
        page: 2,
        pageSize: 20,
        totalCount: 21,
      });

      expect(await screen.findByText('Estudar para a prova')).toBeTruthy();
    });

    it('parâmetro inválido é ignorado e a tela carrega com o padrão, sem erro (CA-18)', async () => {
      const { httpMock } = await setup('tasks?status=xyz&page=abc');

      const req = firstListRequest(httpMock);
      expect(req.request.params.has('status')).toBe(false);
      // `page` sempre vai explícito na chamada à API (a store sempre resolve um número);
      // o que precisa ser saneado é o valor: nunca "abc", sempre o padrão numérico (1).
      expect(req.request.params.get('page')).toBe('1');
      req.flush({ items: [makeTask()], page: 1, pageSize: 20, totalCount: 1 });

      expect(await screen.findByText('Estudar para a prova')).toBeTruthy();
      expect(screen.queryByText(/erro/i)).toBeNull();
    });

    it('page=-1 na URL é saneado para a página 1 (CA-18)', async () => {
      const { httpMock } = await setup('tasks?page=-1');

      const req = firstListRequest(httpMock);
      expect(req.request.params.get('page')).toBe('1');
      req.flush({ items: [makeTask()], page: 1, pageSize: 20, totalCount: 1 });

      expect(await screen.findByText('Estudar para a prova')).toBeTruthy();
    });
  });

  describe('paginação e interação (FE-16)', () => {
    it('alterar um filtro estando na página 3 volta para a página 1 (CA-21)', async () => {
      const { httpMock } = await setup('tasks?page=3');

      firstListRequest(httpMock).flush({
        items: [makeTask()],
        page: 3,
        pageSize: 20,
        totalCount: 60,
      });
      await screen.findByText('Estudar para a prova');

      await userEvent.click(screen.getByRole('radio', { name: 'Pendentes' }));

      const req = await waitFor(() =>
        httpMock.expectOne((r) => r.url === '/api/tasks' && r.params.get('status') === 'pending'),
      );
      expect(req.request.params.get('page')).toBe('1');
      req.flush({ items: [makeTask()], page: 1, pageSize: 20, totalCount: 1 });
    });

    it('a paginação preserva os filtros ativos ao mudar de página (CA-22)', async () => {
      const { httpMock } = await setup('tasks?status=pending');

      firstListRequest(httpMock).flush({
        items: [makeTask({ id: '1', title: 'Página 1' })],
        page: 1,
        pageSize: 20,
        totalCount: 40,
      });
      await screen.findByText('Página 1');

      await userEvent.click(screen.getByRole('button', { name: /próxima/i }));

      const req = await waitFor(() =>
        httpMock.expectOne(
          (r) =>
            r.url === '/api/tasks' &&
            r.params.get('page') === '2' &&
            r.params.get('status') === 'pending',
        ),
      );
      req.flush({
        items: [makeTask({ id: '2', title: 'Página 2' })],
        page: 2,
        pageSize: 20,
        totalCount: 40,
      });

      expect(await screen.findByText('Página 2')).toBeTruthy();
    });

    it('os controles de filtro têm rótulo associado (CA-23)', async () => {
      const { httpMock } = await setup();

      firstListRequest(httpMock).flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });
      await screen.findByText(/você ainda não tem tarefas/i);

      expect(screen.getByRole('radio', { name: 'Todas' })).toBeTruthy();
      expect(screen.getByRole('radio', { name: 'Pendentes' })).toBeTruthy();
      expect(screen.getByRole('radio', { name: 'Concluídas' })).toBeTruthy();
      expect(screen.getByRole('checkbox', { name: 'Baixa' })).toBeTruthy();
      expect(screen.getByRole('checkbox', { name: 'Média' })).toBeTruthy();
      expect(screen.getByRole('checkbox', { name: 'Alta' })).toBeTruthy();
      expect(screen.getByRole('checkbox', { name: /somente atrasadas/i })).toBeTruthy();
      expect(screen.getByLabelText(/buscar/i)).toBeTruthy();
    });

    it('a contagem de resultados é anunciada por leitor de tela (CA-24)', async () => {
      const { httpMock } = await setup('tasks?status=pending');

      firstListRequest(httpMock).flush({
        items: [makeTask(), makeTask({ id: '2' })],
        page: 1,
        pageSize: 20,
        totalCount: 2,
      });

      // `role="status"` também existe no indicador de carregamento (`<app-loading>`); busca
      // pelo texto evita pegar o nó errado enquanto os dois convivem durante a transição.
      const status = await screen.findByText(/2 tarefas encontradas com os filtros aplicados/i);
      expect(status.getAttribute('role')).toBe('status');
    });
  });

  describe('paginação (FE-15)', () => {
    it('"anterior" fica desabilitado na primeira página e "próxima" habilitado quando há mais páginas', async () => {
      const { httpMock } = await setup();

      firstListRequest(httpMock).flush({
        items: [makeTask()],
        page: 1,
        pageSize: 20,
        totalCount: 40,
      });

      expect(await screen.findByText(/página 1 de 2/i)).toBeTruthy();
      expect(screen.getByRole('button', { name: /anterior/i })).toBeDisabled();
      expect(screen.getByRole('button', { name: /próxima/i })).not.toBeDisabled();
    });

    it('não exibe controles de paginação com uma única página', async () => {
      const { httpMock } = await setup();

      firstListRequest(httpMock).flush({
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

  describe('ações por item (FE-19/FE-20)', () => {
    it('concluir uma tarefa recarrega a página em silêncio, sem "piscar" a lista (dívida da Fase 1)', async () => {
      const { httpMock } = await setup();

      firstListRequest(httpMock).flush({
        items: [makeTask({ id: '1' }), makeTask({ id: '2', title: 'Segunda tarefa' })],
        page: 1,
        pageSize: 20,
        totalCount: 2,
      });

      await screen.findByText('Estudar para a prova');
      await screen.findByText('Segunda tarefa');
      await userEvent.click(
        screen.getByRole('checkbox', { name: 'Concluir: Estudar para a prova' }),
      );

      httpMock.expectOne('/api/tasks/1/complete').flush({
        ...makeTask({ id: '1' }),
        status: 'Completed',
        completedAt: '2026-01-02T00:00:00Z',
      });

      // A lista continua visível — nenhum "carregando tarefas" aparece durante o reload.
      expect(screen.queryByText(/carregando tarefas/i)).toBeNull();

      const reloadReq = await waitFor(() =>
        httpMock.expectOne((r) => r.url === '/api/tasks' && r.method === 'GET'),
      );
      reloadReq.flush({
        items: [
          makeTask({ id: '2', title: 'Segunda tarefa' }),
          { ...makeTask({ id: '1' }), status: 'Completed', completedAt: '2026-01-02T00:00:00Z' },
        ],
        page: 1,
        pageSize: 20,
        totalCount: 2,
      });

      expect(await screen.findAllByText('Concluída')).toHaveLength(1);
    });

    it('erro ao concluir exibe a mensagem na linha e a tarefa continua pendente', async () => {
      const { httpMock } = await setup();

      firstListRequest(httpMock).flush({
        items: [makeTask({ id: '1' })],
        page: 1,
        pageSize: 20,
        totalCount: 1,
      });

      await screen.findByText('Estudar para a prova');
      await userEvent.click(screen.getByRole('checkbox', { name: /concluir/i }));

      httpMock
        .expectOne('/api/tasks/1/complete')
        .flush({ errorCode: 'task.already_completed' }, { status: 409, statusText: 'Conflict' });

      expect(await screen.findByText(/já foi concluída em outro lugar/i)).toBeTruthy();
      expect(screen.getByRole('checkbox', { name: /concluir/i })).not.toBeChecked();
    });

    it('remover uma tarefa, após confirmar, faz o item sumir da lista', async () => {
      const { httpMock } = await setup();

      firstListRequest(httpMock).flush({
        items: [makeTask({ id: '1' })],
        page: 1,
        pageSize: 20,
        totalCount: 1,
      });

      await screen.findByText('Estudar para a prova');
      await userEvent.click(screen.getByRole('button', { name: /remover/i }));
      await userEvent.click(screen.getByRole('button', { name: /^remover$/i }));

      httpMock.expectOne('/api/tasks/1').flush(null, { status: 204, statusText: 'No Content' });

      await waitFor(() => expect(screen.queryByText('Estudar para a prova')).toBeNull());
    });

    it('remover o único item da página 3 recarrega a página 2 (CA-10 de FE-20)', async () => {
      const { httpMock } = await setup('tasks?page=3');

      firstListRequest(httpMock).flush({
        items: [makeTask({ id: '1' })],
        page: 3,
        pageSize: 20,
        totalCount: 41,
      });

      await screen.findByText('Estudar para a prova');
      await userEvent.click(screen.getByRole('button', { name: /remover/i }));
      await userEvent.click(screen.getByRole('button', { name: /^remover$/i }));

      httpMock.expectOne('/api/tasks/1').flush(null, { status: 204, statusText: 'No Content' });

      const req = await waitFor(() =>
        httpMock.expectOne((r) => r.url === '/api/tasks' && r.params.get('page') === '2'),
      );
      req.flush({ items: [makeTask({ id: '2' })], page: 2, pageSize: 20, totalCount: 40 });

      expect(await screen.findByText('Estudar para a prova')).toBeTruthy();
    });
  });

  describe('404 em concluir/reabrir/remover (issue #9)', () => {
    const NOT_FOUND = 'Tarefa não encontrada.';
    const notFound = { status: 404, statusText: 'Not Found' };

    async function loadTasks(items: TaskResponse[]) {
      const ctx = await setup();
      firstListRequest(ctx.httpMock).flush({
        items,
        page: 1,
        pageSize: 20,
        totalCount: items.length,
      });
      await screen.findByText('Estudar para a prova');
      return ctx;
    }

    it('concluir com 404 tira o item e avisa na página', async () => {
      const { httpMock } = await loadTasks([
        makeTask({ id: '1' }),
        makeTask({ id: '2', title: 'Segunda' }),
      ]);
      await userEvent.click(
        screen.getByRole('checkbox', { name: 'Concluir: Estudar para a prova' }),
      );
      httpMock.expectOne('/api/tasks/1/complete').flush({}, notFound);

      expect(await screen.findByText(NOT_FOUND)).toBeTruthy();
      expect(screen.queryByText('Estudar para a prova')).toBeNull();
    });

    it('reabrir com 404 tira o item e avisa na página', async () => {
      const { httpMock } = await loadTasks([
        makeTask({ id: '1', status: 'Completed', completedAt: '2026-01-02T00:00:00Z' }),
        makeTask({ id: '2', title: 'Segunda' }),
      ]);
      await userEvent.click(
        screen.getByRole('checkbox', { name: 'Reabrir: Estudar para a prova' }),
      );
      httpMock.expectOne('/api/tasks/1/reopen').flush({}, notFound);

      expect(await screen.findByText(NOT_FOUND)).toBeTruthy();
      expect(screen.queryByText('Estudar para a prova')).toBeNull();
    });

    it('remover com 404 tira o item e avisa na página', async () => {
      const { httpMock } = await loadTasks([
        makeTask({ id: '1' }),
        makeTask({ id: '2', title: 'Segunda' }),
      ]);
      await userEvent.click(screen.getAllByRole('button', { name: /remover/i })[0]!);
      await userEvent.click(screen.getByRole('button', { name: /^remover$/i }));
      httpMock.expectOne('/api/tasks/1').flush({}, notFound);

      expect(await screen.findByText(NOT_FOUND)).toBeTruthy();
      expect(screen.queryByText('Estudar para a prova')).toBeNull();
    });

    it('404 no único item deixa a lista vazia e o aviso continua visível', async () => {
      const { httpMock } = await loadTasks([makeTask({ id: '1' })]);
      await userEvent.click(screen.getByRole('checkbox', { name: /concluir/i }));
      httpMock.expectOne('/api/tasks/1/complete').flush({}, notFound);

      expect(await screen.findByText('Você ainda não tem tarefas.')).toBeTruthy();
      expect(screen.getByText(NOT_FOUND)).toBeTruthy();
    });

    it('iniciar outra ação limpa o aviso', async () => {
      const { httpMock } = await loadTasks([
        makeTask({ id: '1' }),
        makeTask({ id: '2', title: 'Segunda' }),
      ]);
      await userEvent.click(
        screen.getByRole('checkbox', { name: 'Concluir: Estudar para a prova' }),
      );
      httpMock.expectOne('/api/tasks/1/complete').flush({}, notFound);
      await screen.findByText(NOT_FOUND);

      await userEvent.click(screen.getByRole('checkbox', { name: 'Concluir: Segunda' }));
      httpMock
        .expectOne('/api/tasks/2/complete')
        .flush({ errorCode: 'task.already_completed' }, { status: 409, statusText: 'Conflict' });

      await waitFor(() => expect(screen.queryByText(NOT_FOUND)).toBeNull());
    });

    it('erro 500 continua na linha do item, não no aviso da página', async () => {
      const { httpMock } = await loadTasks([makeTask({ id: '1' })]);
      await userEvent.click(screen.getByRole('checkbox', { name: /concluir/i }));
      httpMock
        .expectOne('/api/tasks/1/complete')
        .flush({}, { status: 500, statusText: 'Server Error' });

      const alert = await screen.findByRole('alert');
      expect(alert.closest('app-task-item')).not.toBeNull();
      expect(screen.getAllByRole('alert')).toHaveLength(1);
      expect(screen.getByText('Estudar para a prova')).toBeTruthy();
    });
  });

  describe('foco quando o item sai da lista e na troca de página (issue #10)', () => {
    const three = [
      makeTask({ id: '1', title: 'Primeira' }),
      makeTask({ id: '2', title: 'Segunda' }),
      makeTask({ id: '3', title: 'Terceira' }),
    ];

    async function loadList(items: TaskResponse[], route = 'tasks', totalCount = items.length) {
      const ctx = await setup(route);
      firstListRequest(ctx.httpMock).flush({ items, page: 1, pageSize: 20, totalCount });
      await screen.findByText(items[0]!.title);
      return ctx;
    }

    async function removeItem(title: string) {
      await userEvent.click(screen.getByRole('button', { name: `Remover: ${title}` }));
      await userEvent.click(screen.getByRole('button', { name: /^remover$/i }));
    }

    const title = (text: string) => screen.getByRole('heading', { name: text });
    const noReload = (httpMock: HttpTestingController) =>
      httpMock
        .match((r) => r.url === '/api/tasks' && r.method === 'GET')
        .forEach((r) => r.flush({ items: [], page: 1, pageSize: 20, totalCount: 0 }));

    it('remover o item do meio leva o foco ao título do seguinte', async () => {
      const { httpMock } = await loadList(three);
      await removeItem('Segunda');
      httpMock.expectOne('/api/tasks/2').flush(null, { status: 204, statusText: 'No Content' });

      await waitFor(() => expect(document.activeElement).toBe(title('Terceira')));
    });

    it('remover o último leva o foco ao título do anterior', async () => {
      const { httpMock } = await loadList(three);
      await removeItem('Terceira');
      httpMock.expectOne('/api/tasks/3').flush(null, { status: 204, statusText: 'No Content' });

      await waitFor(() => expect(document.activeElement).toBe(title('Segunda')));
    });

    it('remover o único item leva o foco ao <h1>', async () => {
      const { httpMock } = await loadList([makeTask({ id: '1', title: 'Única' })]);
      await removeItem('Única');
      httpMock.expectOne('/api/tasks/1').flush(null, { status: 204, statusText: 'No Content' });

      await waitFor(() =>
        expect(document.activeElement).toBe(screen.getByRole('heading', { level: 1 })),
      );
    });

    it('concluir com 404 leva o foco ao título do seguinte', async () => {
      const { httpMock } = await loadList(three);
      await userEvent.click(screen.getByRole('checkbox', { name: 'Concluir: Primeira' }));
      httpMock
        .expectOne('/api/tasks/1/complete')
        .flush({}, { status: 404, statusText: 'Not Found' });

      await waitFor(() => expect(document.activeElement).toBe(title('Segunda')));
    });

    it('com filtro Pendentes, concluir tira o item e leva o foco ao título do seguinte', async () => {
      const { httpMock } = await loadList(three, 'tasks?status=pending');
      await userEvent.click(screen.getByRole('checkbox', { name: 'Concluir: Primeira' }));
      httpMock.expectOne('/api/tasks/1/complete').flush({
        ...three[0]!,
        status: 'Completed',
        completedAt: '2026-01-02T00:00:00Z',
      });

      await waitFor(() => expect(document.activeElement).toBe(title('Segunda')));
      noReload(httpMock);
    });

    it('sem filtro, concluir não move o foco do checkbox', async () => {
      const { httpMock } = await loadList(three);
      const checkbox = screen.getByRole('checkbox', { name: 'Concluir: Primeira' });
      await userEvent.click(checkbox);
      httpMock.expectOne('/api/tasks/1/complete').flush({
        ...three[0]!,
        status: 'Completed',
        completedAt: '2026-01-02T00:00:00Z',
      });
      await screen.findAllByText('Concluída');

      expect(document.activeElement).toBe(checkbox);
      noReload(httpMock);
    });

    it('trocar de página leva o foco ao primeiro item novo e anuncia a página', async () => {
      const { httpMock } = await loadList(three, 'tasks', 60);
      await userEvent.click(screen.getByRole('button', { name: /próxima/i }));
      const req = await waitFor(() =>
        httpMock.expectOne((r) => r.url === '/api/tasks' && r.params.get('page') === '2'),
      );
      req.flush({
        items: [makeTask({ id: '4', title: 'Quarta' }), makeTask({ id: '5', title: 'Quinta' })],
        page: 2,
        pageSize: 20,
        totalCount: 60,
      });

      await waitFor(() => expect(document.activeElement).toBe(title('Quarta')));
      expect(screen.getByText(/página 2 de 3/i).getAttribute('role')).toBe('status');
    });

    it('carga inicial e mudança de filtro não roubam o foco', async () => {
      const { httpMock } = await loadList(three);
      const isTarget = () => ['H1', 'H2'].includes(document.activeElement?.tagName ?? '');
      expect(isTarget()).toBe(false);

      await userEvent.click(screen.getByRole('radio', { name: 'Pendentes' }));
      const req = await waitFor(() =>
        httpMock.expectOne((r) => r.url === '/api/tasks' && r.params.get('status') === 'pending'),
      );
      req.flush({ items: three, page: 1, pageSize: 20, totalCount: 3 });
      await screen.findByText('Primeira');

      expect(isTarget()).toBe(false);
    });
  });

  describe('acessibilidade: link Editar e anúncio da remoção (issue #11)', () => {
    async function loadTwo() {
      const ctx = await setup();
      firstListRequest(ctx.httpMock).flush({
        items: [makeTask({ id: '1' }), makeTask({ id: '2', title: 'Segunda' })],
        page: 1,
        pageSize: 20,
        totalCount: 2,
      });
      await screen.findByText('Segunda');
      return ctx;
    }

    const region = () => document.querySelector('.tasks-page__announcement') as HTMLElement | null;

    async function confirmRemove(title: string) {
      await userEvent.click(screen.getByRole('button', { name: `Remover: ${title}` }));
      await userEvent.click(screen.getByRole('button', { name: /^remover$/i }));
    }

    it('cada link Editar tem nome acessível distinto', async () => {
      const { httpMock } = await loadTwo();

      expect(screen.getByRole('link', { name: /editar: .*estudar para a prova/i })).toBeTruthy();
      expect(screen.getByRole('link', { name: /editar: .*segunda/i })).toBeTruthy();
      httpMock.verify();
    });

    it('a região viva da página existe vazia antes de qualquer remoção', async () => {
      const { httpMock } = await loadTwo();

      expect(region()).not.toBeNull();
      expect(region()?.getAttribute('aria-live')).toBe('polite');
      expect(region()?.textContent?.trim()).toBe('');
      httpMock.verify();
    });

    it('remoção com sucesso anuncia a tarefa removida, mesmo com a lista vazia', async () => {
      const ctx = await setup();
      firstListRequest(ctx.httpMock).flush({
        items: [makeTask({ id: '1' })],
        page: 1,
        pageSize: 20,
        totalCount: 1,
      });
      await screen.findByText('Estudar para a prova');
      await confirmRemove('Estudar para a prova');
      ctx.httpMock.expectOne('/api/tasks/1').flush(null, { status: 204, statusText: 'No Content' });

      await waitFor(() => expect(region()?.textContent).toMatch(/removida/i));
      expect(region()?.textContent).toContain('Estudar para a prova');
      expect(screen.getByText('Você ainda não tem tarefas.')).toBeTruthy();
    });

    it.each([
      [500, 'Server Error'],
      [404, 'Not Found'],
    ])('remoção com erro %i não anuncia "removida"', async (status, statusText) => {
      const { httpMock } = await loadTwo();
      await confirmRemove('Estudar para a prova');
      httpMock.expectOne('/api/tasks/1').flush({}, { status, statusText });

      await screen.findAllByRole('alert');
      expect(region()?.textContent ?? '').not.toMatch(/removida/i);
    });

    it('iniciar outra ação limpa o anúncio', async () => {
      const { httpMock } = await loadTwo();
      await confirmRemove('Estudar para a prova');
      httpMock.expectOne('/api/tasks/1').flush(null, { status: 204, statusText: 'No Content' });
      await waitFor(() => expect(region()?.textContent).toMatch(/removida/i));

      await userEvent.click(screen.getByRole('checkbox', { name: 'Concluir: Segunda' }));
      httpMock.expectOne('/api/tasks/2/complete').flush(makeTask({ id: '2', status: 'Completed' }));

      await waitFor(() => expect(region()?.textContent?.trim()).toBe(''));
    });
  });
});
