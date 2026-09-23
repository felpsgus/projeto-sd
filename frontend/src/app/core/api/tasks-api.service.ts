import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ApiClient } from './api-client';
import { CreateTaskRequest, ListTasksQuery, PagedResult, TaskResponse } from './models/task.models';

const TASKS_PATH = '/api/tasks';

/**
 * Serviço de recurso para tarefas (recorte do T2: criar, listar, obter — sem editar,
 * concluir/reabrir ou remover, que ficam para a próxima onda em `features/tasks/`).
 */
@Injectable({ providedIn: 'root' })
export class TasksApi {
  private readonly apiClient = inject(ApiClient);

  create(request: CreateTaskRequest): Observable<TaskResponse> {
    return this.apiClient.post<TaskResponse>(TASKS_PATH, request);
  }

  list(query: ListTasksQuery = {}): Observable<PagedResult<TaskResponse>> {
    const params: Record<string, string | number> = {};
    if (query.page !== undefined) {
      params['page'] = query.page;
    }
    if (query.pageSize !== undefined) {
      params['pageSize'] = query.pageSize;
    }
    return this.apiClient.get<PagedResult<TaskResponse>>(TASKS_PATH, params);
  }

  getById(id: string): Observable<TaskResponse> {
    return this.apiClient.get<TaskResponse>(`${TASKS_PATH}/${id}`);
  }
}
