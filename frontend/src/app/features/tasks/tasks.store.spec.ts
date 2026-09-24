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
});
