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
});
