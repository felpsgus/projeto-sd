import { TaskResponse } from '../../app/core/api/models/task.models';

/** Exemplo de payload real de `TaskHttpResponse` — Gateway. */
export const TASK_RESPONSE_FIXTURE: TaskResponse = {
  id: '3e2f9c2a-1b4a-4e9e-9c1a-6f5b2a1d0e11',
  title: 'Estudar para a apresentação',
  description: 'Revisar os slides do T2',
  priority: 'High',
  status: 'Pending',
  dueDate: '2026-10-22',
  completedAt: null,
  isOverdue: false,
  createdAt: '2026-09-22T20:00:00Z',
  updatedAt: '2026-09-22T20:00:00Z',
};
