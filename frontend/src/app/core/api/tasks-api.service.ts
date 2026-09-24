import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ApiClient } from './api-client';
import {
  CreateTaskRequest,
  ListTasksQuery,
  PagedResult,
  TaskResponse,
  UpdateTaskRequest,
} from './models/task.models';

const TASKS_PATH = '/api/tasks';

/**
 * Serviço de recurso para tarefas: criar, listar, obter, editar (substituição via `PUT`),
 * concluir, reabrir e remover (FE-14/17/18/19/20). Nenhum componente injeta este serviço
 * diretamente — sempre por `TasksStore`.
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

  /** `PUT /api/tasks/{id}` — substituição total (BE-19); `request` sempre traz os quatro campos. */
  update(id: string, request: UpdateTaskRequest): Observable<TaskResponse> {
    return this.apiClient.put<TaskResponse>(`${TASKS_PATH}/${id}`, request);
  }

  /** `POST /api/tasks/{id}/complete` — sem corpo de requisição (BE-20). */
  complete(id: string): Observable<TaskResponse> {
    return this.apiClient.post<TaskResponse>(`${TASKS_PATH}/${id}/complete`, null);
  }

  /** `POST /api/tasks/{id}/reopen` — sem corpo de requisição (BE-20). */
  reopen(id: string): Observable<TaskResponse> {
    return this.apiClient.post<TaskResponse>(`${TASKS_PATH}/${id}/reopen`, null);
  }

  /** `DELETE /api/tasks/{id}` — soft delete no servidor; 204 sem corpo (BE-21). */
  remove(id: string): Observable<void> {
    return this.apiClient.delete<void>(`${TASKS_PATH}/${id}`);
  }
}
