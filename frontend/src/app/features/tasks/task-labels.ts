import { TaskPriority, TaskStatus } from '../../core/api/models/task.models';

/**
 * Rótulos textuais em pt-BR para prioridade e situação (FE-15, CA-05) — a cor nunca é a
 * única forma de comunicar prioridade/situação/atraso (falha para daltônicos e impressão).
 */
export const PRIORITY_LABELS: Readonly<Record<TaskPriority, string>> = {
  Low: 'Baixa',
  Medium: 'Média',
  High: 'Alta',
};

export const STATUS_LABELS: Readonly<Record<TaskStatus, string>> = {
  Pending: 'Pendente',
  Completed: 'Concluída',
};
