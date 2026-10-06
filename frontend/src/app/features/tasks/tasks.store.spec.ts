import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { TasksStore } from './tasks.store';
import { SessionStore } from '../../core/auth/session-store';
import { errorInterceptor } from '../../core/errors/error.interceptor';
import { TaskResponse } from '../../core/api/models/task.models';

function makeTask(id: string): TaskResponse {
  return {
    id,
    title: `Tarefa ${id}`,
    description: null,
    priority: 'Medium',
    status: 'Pending',
    dueDate: null,
    completedAt: null,
    isOverdue: false,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
  };
}

describe('TasksStore', () => {
  let store: TasksStore;
  let httpMock: HttpTestingController;
  let sessionStore: SessionStore;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    store = TestBed.inject(TasksStore);
    httpMock = TestBed.inject(HttpTestingController);
    sessionStore = TestBed.inject(SessionStore);
  });

  afterEach(() => httpMock.verify());

  it('load() fica em loading e depois popula items/paginação em sucesso (CA-01/CA-02)', () => {
    store.load(1, 20);

    expect(store.status()).toBe('loading');

    const req = httpMock.expectOne((r) => r.url === '/api/tasks');
    req.flush({ items: [makeTask('1'), makeTask('2')], page: 1, pageSize: 20, totalCount: 2 });

    expect(store.status()).toBe('success');
    expect(store.items().length).toBe(2);
    expect(store.page()).toBe(1);
    expect(store.pageSize()).toBe(20);
    expect(store.totalCount()).toBe(2);
    expect(store.totalPages()).toBe(1);
  });

  it('totalPages é calculado no cliente a partir de totalCount e pageSize', () => {
    store.load(1, 20);

    httpMock
      .expectOne((r) => r.url === '/api/tasks')
      .flush({ items: [makeTask('1')], page: 1, pageSize: 20, totalCount: 45 });

    expect(store.totalPages()).toBe(3);
  });

  it('load() de outra página envia page/pageSize corretos e atualiza o estado', () => {
    store.load(2, 10);

    const req = httpMock.expectOne((r) => r.url === '/api/tasks');
    expect(req.request.params.get('page')).toBe('2');
    expect(req.request.params.get('pageSize')).toBe('10');
    req.flush({ items: [], page: 2, pageSize: 10, totalCount: 0 });

    expect(store.page()).toBe(2);
    expect(store.pageSize()).toBe(10);
    expect(store.isEmpty()).toBe(true);
  });

  it('erro de carregamento deixa status "error" com o AppError preenchido', () => {
    store.load(1, 20);

    httpMock
      .expectOne((r) => r.url === '/api/tasks')
      .flush({ errorCode: 'unknown' }, { status: 500, statusText: 'Server Error' });

    expect(store.status()).toBe('error');
    expect(store.error()).not.toBeNull();
    expect(store.items()).toEqual([]);
  });

  it('create() chama POST /api/tasks e devolve a tarefa criada', () => {
    let created: TaskResponse | undefined;

    store.create({ title: 'Nova', description: null, priority: null, dueDate: null }).subscribe({
      next: (task) => (created = task),
    });

    const req = httpMock.expectOne('/api/tasks');
    expect(req.request.method).toBe('POST');
    req.flush(makeTask('9'));

    expect(created?.id).toBe('9');
  });

  it('getById() chama GET /api/tasks/{id}', () => {
    store.getById('1').subscribe();

    const req = httpMock.expectOne('/api/tasks/1');
    expect(req.request.method).toBe('GET');
    req.flush(makeTask('1'));
  });

  it('isEmpty é falso durante o carregamento, mesmo sem itens ainda', () => {
    store.load(1, 20);

    expect(store.isEmpty()).toBe(false);

    httpMock
      .expectOne((r) => r.url === '/api/tasks')
      .flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });
  });

  it('duas chamadas de load em sequência não deixam a lista com o resultado da primeira', () => {
    store.load(1, 20);
    const firstReq = httpMock.expectOne((r) => r.url === '/api/tasks');

    store.load(1, 20);
    const secondReq = httpMock.expectOne((r) => r.url === '/api/tasks');

    secondReq.flush({ items: [makeTask('2')], page: 1, pageSize: 20, totalCount: 1 });
    firstReq.flush({ items: [makeTask('1')], page: 1, pageSize: 20, totalCount: 1 });

    expect(store.items().map((t) => t.id)).toEqual(['2']);
  });

  it('update() chama PUT /api/tasks/{id} e substitui o item na lista pela resposta do servidor', () => {
    store.load(1, 20);
    httpMock
      .expectOne((r) => r.url === '/api/tasks')
      .flush({ items: [makeTask('1'), makeTask('2')], page: 1, pageSize: 20, totalCount: 2 });

    const updated = { ...makeTask('1'), title: 'Título editado' };
    store
      .update('1', { title: 'Título editado', description: null, priority: 'High', dueDate: null })
      .subscribe();

    const req = httpMock.expectOne('/api/tasks/1');
    expect(req.request.method).toBe('PUT');
    req.flush(updated);

    expect(store.items().find((t) => t.id === '1')?.title).toBe('Título editado');
    // O item que não mudou mantém a mesma referência (nenhum "piscar" no resto da lista).
    expect(store.items().find((t) => t.id === '2')).toBe(store.items()[1]);
  });

  it('complete() atualiza o item otimisticamente antes da resposta do servidor (FD-06)', () => {
    store.load(1, 20);
    httpMock
      .expectOne((r) => r.url === '/api/tasks')
      .flush({ items: [makeTask('1')], page: 1, pageSize: 20, totalCount: 1 });

    store.complete('1').subscribe();

    expect(store.items()[0]!.status).toBe('Completed');

    const req = httpMock.expectOne('/api/tasks/1/complete');
    req.flush({ ...makeTask('1'), status: 'Completed', completedAt: '2026-01-02T00:00:00Z' });

    expect(store.items()[0]!.completedAt).toBe('2026-01-02T00:00:00Z');

    // Recarregamento silencioso pós-transição (FE-16): a página atual é buscada de novo para
    // refletir a nova posição do item (RN-LIST-06), sem passar `status` por 'loading'.
    expect(store.status()).toBe('success');
    httpMock
      .expectOne((r) => r.url === '/api/tasks')
      .flush({
        items: [{ ...makeTask('1'), status: 'Completed', completedAt: '2026-01-02T00:00:00Z' }],
        page: 1,
        pageSize: 20,
        totalCount: 1,
      });
  });

  it('complete() recarrega a página atual em silêncio, sem alternar status para loading', () => {
    store.load(1, 20);
    httpMock
      .expectOne((r) => r.url === '/api/tasks')
      .flush({ items: [makeTask('1'), makeTask('2')], page: 1, pageSize: 20, totalCount: 2 });

    store.complete('1').subscribe();
    httpMock
      .expectOne('/api/tasks/1/complete')
      .flush({ ...makeTask('1'), status: 'Completed', completedAt: '2026-01-02T00:00:00Z' });

    // A lista não desaparece atrás de um spinner enquanto o reload silencioso está em voo.
    expect(store.status()).toBe('success');

    const reloadReq = httpMock.expectOne((r) => r.url === '/api/tasks');
    reloadReq.flush({
      items: [makeTask('2'), { ...makeTask('1'), status: 'Completed' }],
      page: 1,
      pageSize: 20,
      totalCount: 2,
    });

    // A nova ordem (pendente antes de concluída, RN-LIST-06) vem do servidor.
    expect(store.items().map((t) => t.id)).toEqual(['2', '1']);
    expect(store.status()).toBe('success');
  });

  it('erro no recarregamento silencioso pós-complete não derruba a tela para "error"', () => {
    store.load(1, 20);
    httpMock
      .expectOne((r) => r.url === '/api/tasks')
      .flush({ items: [makeTask('1')], page: 1, pageSize: 20, totalCount: 1 });

    store.complete('1').subscribe();
    httpMock
      .expectOne('/api/tasks/1/complete')
      .flush({ ...makeTask('1'), status: 'Completed', completedAt: '2026-01-02T00:00:00Z' });

    httpMock
      .expectOne((r) => r.url === '/api/tasks')
      .flush({ errorCode: 'unknown' }, { status: 500, statusText: 'Server Error' });

    expect(store.status()).toBe('success');
    expect(store.items()[0]!.status).toBe('Completed');
  });

  it('complete() reverte para "Pending" se a chamada falhar (rollback obrigatório)', () => {
    store.load(1, 20);
    httpMock
      .expectOne((r) => r.url === '/api/tasks')
      .flush({ items: [makeTask('1')], page: 1, pageSize: 20, totalCount: 1 });

    let receivedError: unknown;
    store.complete('1').subscribe({ error: (error) => (receivedError = error) });

    expect(store.items()[0]!.status).toBe('Completed');

    httpMock
      .expectOne('/api/tasks/1/complete')
      .flush({ errorCode: 'task.already_completed' }, { status: 409, statusText: 'Conflict' });

    expect(store.items()[0]!.status).toBe('Pending');
    expect(receivedError).toBeTruthy();
  });

  it('reopen() reverte para "Completed" se a chamada falhar (rollback obrigatório)', () => {
    store.load(1, 20);
    httpMock
      .expectOne((r) => r.url === '/api/tasks')
      .flush({
        items: [{ ...makeTask('1'), status: 'Completed', completedAt: '2026-01-01T00:00:00Z' }],
        page: 1,
        pageSize: 20,
        totalCount: 1,
      });

    store.reopen('1').subscribe({ error: () => undefined });

    expect(store.items()[0]!.status).toBe('Pending');

    httpMock
      .expectOne('/api/tasks/1/reopen')
      .flush({ errorCode: 'task.not_completed' }, { status: 409, statusText: 'Conflict' });

    expect(store.items()[0]!.status).toBe('Completed');
    expect(store.items()[0]!.completedAt).toBe('2026-01-01T00:00:00Z');
  });

  it('reopen() em sucesso também recarrega a página atual em silêncio (RN-LIST-06)', () => {
    store.load(1, 20);
    httpMock
      .expectOne((r) => r.url === '/api/tasks')
      .flush({
        items: [{ ...makeTask('1'), status: 'Completed' }],
        page: 1,
        pageSize: 20,
        totalCount: 1,
      });

    store.reopen('1').subscribe();
    httpMock
      .expectOne('/api/tasks/1/reopen')
      .flush({ ...makeTask('1'), status: 'Pending', completedAt: null });

    expect(store.status()).toBe('success');
    httpMock
      .expectOne((r) => r.url === '/api/tasks')
      .flush({ items: [makeTask('1')], page: 1, pageSize: 20, totalCount: 1 });

    expect(store.items()[0]!.status).toBe('Pending');
  });

  it('complete() em 404 remove o item da lista e decrementa totalCount (RN-AUTZ-03)', () => {
    store.load(1, 20);
    httpMock
      .expectOne((r) => r.url === '/api/tasks')
      .flush({ items: [makeTask('1'), makeTask('2')], page: 1, pageSize: 20, totalCount: 2 });

    store.complete('1').subscribe({ error: () => undefined });

    httpMock
      .expectOne('/api/tasks/1/complete')
      .flush(null, { status: 404, statusText: 'Not Found' });

    expect(store.items().map((t) => t.id)).toEqual(['2']);
    expect(store.totalCount()).toBe(1);
  });

  it('remove() só tira o item da lista depois do 204 — sem otimismo', () => {
    store.load(1, 20);
    httpMock
      .expectOne((r) => r.url === '/api/tasks')
      .flush({ items: [makeTask('1')], page: 1, pageSize: 20, totalCount: 1 });

    store.remove('1').subscribe();

    expect(store.items().length).toBe(1);

    httpMock.expectOne('/api/tasks/1').flush(null, { status: 204, statusText: 'No Content' });

    expect(store.items().length).toBe(0);
    expect(store.totalCount()).toBe(0);
  });

  it('remove() em erro de rede mantém a tarefa na lista (CA-17 de FE-20)', () => {
    store.load(1, 20);
    httpMock
      .expectOne((r) => r.url === '/api/tasks')
      .flush({ items: [makeTask('1')], page: 1, pageSize: 20, totalCount: 1 });

    let receivedError: unknown;
    store.remove('1').subscribe({ error: (error) => (receivedError = error) });

    httpMock.expectOne('/api/tasks/1').error(new ProgressEvent('error'));

    expect(store.items().length).toBe(1);
    expect(receivedError).toBeTruthy();
  });

  it('remove() em 404 remove o item mesmo assim (RN-AUTZ-03)', () => {
    store.load(1, 20);
    httpMock
      .expectOne((r) => r.url === '/api/tasks')
      .flush({ items: [makeTask('1')], page: 1, pageSize: 20, totalCount: 1 });

    store.remove('1').subscribe({ error: () => undefined });

    httpMock.expectOne('/api/tasks/1').flush(null, { status: 404, statusText: 'Not Found' });

    expect(store.items().length).toBe(0);
  });

  it('encerrar a sessão limpa items, paginação e erro (limpeza entre usuários)', () => {
    sessionStore.startSession({ accessToken: 'tok', expiresAt: '2026-01-01T00:15:00Z' }, 'a@b.com');

    store.load(1, 20);
    httpMock
      .expectOne((r) => r.url === '/api/tasks')
      .flush({ items: [makeTask('1')], page: 1, pageSize: 20, totalCount: 1 });

    expect(store.items().length).toBe(1);

    sessionStore.endSession('user_logout');
    TestBed.flushEffects();

    expect(store.items()).toEqual([]);
    expect(store.totalCount()).toBe(0);
    expect(store.status()).toBe('idle');
    expect(store.error()).toBeNull();
  });

  describe('saída imediata da visão filtrada em complete/reopen (FE-19 CA-06)', () => {
    const completedTask = (id: string): TaskResponse => ({
      ...makeTask(id),
      status: 'Completed',
      completedAt: '2026-01-01T00:00:00Z',
    });

    function loadWith(
      filters: Partial<Parameters<TasksStore['load']>[2]>,
      items: TaskResponse[],
    ): void {
      store.load(1, 20, { status: 'all', priority: [], overdue: null, search: '', ...filters });
      httpMock
        .expectOne((r) => r.url === '/api/tasks')
        .flush({ items, page: 1, pageSize: 20, totalCount: items.length });
    }

    const threePending = () => [makeTask('1'), makeTask('2'), makeTask('3')];

    it('filtro Pendentes: complete() tira o item da lista antes da resposta e decrementa totalCount', () => {
      loadWith({ status: 'pending' }, threePending());

      store.complete('2').subscribe();

      expect(store.items().map((t) => t.id)).toEqual(['1', '3']);
      expect(store.totalCount()).toBe(2);

      httpMock.expectOne('/api/tasks/2/complete').flush(completedTask('2'));
      httpMock
        .expectOne((r) => r.url === '/api/tasks')
        .flush({ items: [makeTask('1'), makeTask('3')], page: 1, pageSize: 20, totalCount: 2 });
    });

    it('filtro Pendentes: complete() com 500 devolve o item ao mesmo índice, no estado anterior', () => {
      loadWith({ status: 'pending' }, threePending());

      store.complete('2').subscribe({ error: () => undefined });
      httpMock
        .expectOne('/api/tasks/2/complete')
        .flush({ errorCode: 'unknown' }, { status: 500, statusText: 'Server Error' });

      expect(store.items().map((t) => t.id)).toEqual(['1', '2', '3']);
      expect(store.items()[1]!.status).toBe('Pending');
      expect(store.totalCount()).toBe(3);
    });

    it('filtro Pendentes: complete() com 404 mantém o item fora da lista', () => {
      loadWith({ status: 'pending' }, threePending());

      store.complete('2').subscribe({ error: () => undefined });
      httpMock
        .expectOne('/api/tasks/2/complete')
        .flush(null, { status: 404, statusText: 'Not Found' });

      expect(store.items().map((t) => t.id)).toEqual(['1', '3']);
      expect(store.totalCount()).toBe(2);
    });

    it('filtro Concluídas: reopen() tira o item imediatamente; com falha, volta à posição', () => {
      loadWith({ status: 'completed' }, [
        completedTask('1'),
        completedTask('2'),
        completedTask('3'),
      ]);

      store.reopen('2').subscribe({ error: () => undefined });

      expect(store.items().map((t) => t.id)).toEqual(['1', '3']);
      expect(store.totalCount()).toBe(2);

      httpMock
        .expectOne('/api/tasks/2/reopen')
        .flush({ errorCode: 'unknown' }, { status: 500, statusText: 'Server Error' });

      expect(store.items().map((t) => t.id)).toEqual(['1', '2', '3']);
      expect(store.items()[1]!.status).toBe('Completed');
      expect(store.totalCount()).toBe(3);
    });

    it('filtro de atrasadas: complete() tira o item imediatamente', () => {
      loadWith(
        { overdue: true },
        threePending().map((t) => ({ ...t, isOverdue: true })),
      );

      store.complete('2').subscribe();

      expect(store.items().map((t) => t.id)).toEqual(['1', '3']);
      expect(store.totalCount()).toBe(2);

      httpMock.expectOne('/api/tasks/2/complete').flush(completedTask('2'));
      httpMock
        .expectOne((r) => r.url === '/api/tasks')
        .flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });
    });

    it('sem filtros: complete() mantém o item na lista com o estado novo', () => {
      loadWith({}, threePending());

      store.complete('2').subscribe();

      expect(store.items().map((t) => t.id)).toEqual(['1', '2', '3']);
      expect(store.items()[1]!.status).toBe('Completed');
      expect(store.totalCount()).toBe(3);

      httpMock.expectOne('/api/tasks/2/complete').flush(completedTask('2'));
      httpMock
        .expectOne((r) => r.url === '/api/tasks')
        .flush({ items: threePending(), page: 1, pageSize: 20, totalCount: 3 });
    });
  });

  describe('filtros (FE-16)', () => {
    it('load() sem filtro não envia status, priority, overdue nem search', () => {
      store.load(1, 20);

      const req = httpMock.expectOne((r) => r.url === '/api/tasks');
      expect(req.request.params.has('status')).toBe(false);
      expect(req.request.params.has('priority')).toBe(false);
      expect(req.request.params.has('overdue')).toBe(false);
      expect(req.request.params.has('search')).toBe(false);
      req.flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });
    });

    it('load() com status="pending" envia status na chamada (CA-01)', () => {
      store.load(1, 20, { status: 'pending', priority: [], overdue: null, search: '' });

      const req = httpMock.expectOne((r) => r.url === '/api/tasks');
      expect(req.request.params.get('status')).toBe('pending');
      req.flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });
    });

    it('load() com prioridades envia um "priority" repetido por valor (CA-03)', () => {
      store.load(1, 20, {
        status: 'all',
        priority: ['low', 'high'],
        overdue: null,
        search: '',
      });

      const req = httpMock.expectOne((r) => r.url === '/api/tasks');
      expect(req.request.params.getAll('priority')).toEqual(['low', 'high']);
      req.flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });
    });

    it('load() com overdue=true envia o parâmetro (CA-04)', () => {
      store.load(1, 20, { status: 'all', priority: [], overdue: true, search: '' });

      const req = httpMock.expectOne((r) => r.url === '/api/tasks');
      expect(req.request.params.get('overdue')).toBe('true');
      req.flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });
    });

    it('load() com busca envia "search" com o termo aparado (CA-08/CA-09)', () => {
      store.load(1, 20, { status: 'all', priority: [], overdue: null, search: '  relatório  ' });

      const req = httpMock.expectOne((r) => r.url === '/api/tasks');
      expect(req.request.params.get('search')).toBe('relatório');
      req.flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });
    });

    it('busca vazia ou só espaços não envia "search" (equivale a ausente)', () => {
      store.load(1, 20, { status: 'all', priority: [], overdue: null, search: '   ' });

      const req = httpMock.expectOne((r) => r.url === '/api/tasks');
      expect(req.request.params.has('search')).toBe(false);
      req.flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });
    });

    it('os filtros combinam numa única chamada (CA-05)', () => {
      store.load(1, 20, {
        status: 'pending',
        priority: ['high'],
        overdue: true,
        search: 'prova',
      });

      const req = httpMock.expectOne((r) => r.url === '/api/tasks');
      expect(req.request.params.get('status')).toBe('pending');
      expect(req.request.params.getAll('priority')).toEqual(['high']);
      expect(req.request.params.get('overdue')).toBe('true');
      expect(req.request.params.get('search')).toBe('prova');
      req.flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });
    });

    it('isEmpty é true sem filtro e zero itens; isFilteredEmpty é true com filtro e zero itens (CA-13)', () => {
      store.load(1, 20, { status: 'pending', priority: [], overdue: null, search: '' });
      httpMock
        .expectOne((r) => r.url === '/api/tasks')
        .flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });

      expect(store.isFilteredEmpty()).toBe(true);
      expect(store.isEmpty()).toBe(false);
    });

    it('isEmpty é true e isFilteredEmpty é false quando não há filtro nem tarefas', () => {
      store.load(1, 20);
      httpMock
        .expectOne((r) => r.url === '/api/tasks')
        .flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });

      expect(store.isEmpty()).toBe(true);
      expect(store.isFilteredEmpty()).toBe(false);
    });

    it('resposta fora de ordem de uma busca anterior não sobrescreve a mais recente (CA-11)', () => {
      store.load(1, 20, { status: 'all', priority: [], overdue: null, search: 'rel' });
      const firstReq = httpMock.expectOne((r) => r.url === '/api/tasks');

      store.load(1, 20, { status: 'all', priority: [], overdue: null, search: 'relatorio' });
      const secondReq = httpMock.expectOne((r) => r.url === '/api/tasks');

      secondReq.flush({ items: [makeTask('2')], page: 1, pageSize: 20, totalCount: 1 });
      firstReq.flush({ items: [makeTask('1')], page: 1, pageSize: 20, totalCount: 1 });

      expect(store.items().map((t) => t.id)).toEqual(['2']);
    });
  });
});
