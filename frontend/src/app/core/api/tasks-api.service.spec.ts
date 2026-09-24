import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { TasksApi } from './tasks-api.service';
import { TaskResponse } from './models/task.models';

const TASK: TaskResponse = {
  id: '1',
  title: 'Estudar',
  description: null,
  priority: 'Medium',
  status: 'Pending',
  dueDate: '2026-01-01',
  completedAt: null,
  isOverdue: false,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
};

describe('TasksApi', () => {
  let tasksApi: TasksApi;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    tasksApi = TestBed.inject(TasksApi);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('cria uma tarefa via POST /api/tasks', () => {
    tasksApi
      .create({ title: 'Estudar', description: null, priority: null, dueDate: null })
      .subscribe();

    const req = httpMock.expectOne('/api/tasks');
    expect(req.request.method).toBe('POST');
    req.flush(TASK);
  });

  it('lista tarefas via GET /api/tasks com page e pageSize na query string', () => {
    tasksApi.list({ page: 2, pageSize: 10 }).subscribe();

    const req = httpMock.expectOne((r) => r.url === '/api/tasks');
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('page')).toBe('2');
    expect(req.request.params.get('pageSize')).toBe('10');
    req.flush({ items: [TASK], page: 2, pageSize: 10, totalCount: 1 });
  });

  it('não inclui page/pageSize na URL quando ausentes', () => {
    tasksApi.list().subscribe();

    const req = httpMock.expectOne((r) => r.url === '/api/tasks');
    expect(req.request.params.keys().length).toBe(0);
    req.flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });
  });

  it('obtém uma tarefa por id via GET /api/tasks/{id}', () => {
    tasksApi.getById('1').subscribe();

    const req = httpMock.expectOne('/api/tasks/1');
    expect(req.request.method).toBe('GET');
    req.flush(TASK);
  });

  it('atualiza uma tarefa via PUT /api/tasks/{id} com os quatro campos', () => {
    tasksApi
      .update('1', {
        title: 'Estudar',
        description: 'Cap. 1',
        priority: 'High',
        dueDate: '2026-02-01',
      })
      .subscribe();

    const req = httpMock.expectOne('/api/tasks/1');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({
      title: 'Estudar',
      description: 'Cap. 1',
      priority: 'High',
      dueDate: '2026-02-01',
    });
    req.flush(TASK);
  });

  it('conclui uma tarefa via POST /api/tasks/{id}/complete sem corpo', () => {
    tasksApi.complete('1').subscribe();

    const req = httpMock.expectOne('/api/tasks/1/complete');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toBeNull();
    req.flush(TASK);
  });

  it('reabre uma tarefa via POST /api/tasks/{id}/reopen sem corpo', () => {
    tasksApi.reopen('1').subscribe();

    const req = httpMock.expectOne('/api/tasks/1/reopen');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toBeNull();
    req.flush(TASK);
  });

  it('remove uma tarefa via DELETE /api/tasks/{id}', () => {
    let completed = false;
    tasksApi.remove('1').subscribe({ complete: () => (completed = true) });

    const req = httpMock.expectOne('/api/tasks/1');
    expect(req.request.method).toBe('DELETE');
    req.flush(null, { status: 204, statusText: 'No Content' });

    expect(completed).toBe(true);
  });
});
