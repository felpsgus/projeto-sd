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

  /**
   * `GET /api/tasks` (FE-15/FE-16). Cada filtro só entra nos parâmetros quando tem um valor
   * que não é o "ausente" da tabela de contrato (`status: 'all'`, `priority: []`, `search`
   * vazio/só espaços) — mandar o valor ausente explicitamente seria ruído na requisição e,
   * no caso de `search`, o backend já trata espaços como ausente mesmo assim (CA-17 de
   * BE-22), então aparar aqui só evita uma viagem de rede inútil.
   */
  list(query: ListTasksQuery = {}): Observable<PagedResult<TaskResponse>> {
    const params: Record<string, string | number | readonly string[]> = {};
    if (query.page !== undefined) {
      params['page'] = query.page;
    }
    if (query.pageSize !== undefined) {
      params['pageSize'] = query.pageSize;
    }
    if (query.status && query.status !== 'all') {
      params['status'] = query.status;
    }
    if (query.priority && query.priority.length > 0) {
      params['priority'] = query.priority;
    }
    if (query.overdue !== undefined) {
      params['overdue'] = String(query.overdue);
    }
    const search = query.search?.trim();
    if (search) {
      params['search'] = search;
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
