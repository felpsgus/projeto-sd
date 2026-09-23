import { AppErrorCode } from './app-error.model';

/**
 * Mapa central de código de erro → mensagem pt-BR (FD-03; FE-03, CA-11). Nenhuma tela
 * escreve uma mensagem de erro fora daqui — inclusive a mensagem genérica de fallback.
 *
 * Catálogo restrito ao que o backend do T2 emite: `auth.invalid_credentials` (401 de
 * login) e `auth.unauthorized` (401 de token ausente/inválido/expirado). Um código novo
 * do backend cai automaticamente na mensagem genérica até ser adicionado aqui — nunca é
 * exibido cru ao usuário (CA-03).
 */
export const ERROR_MESSAGES: Readonly<Record<AppErrorCode, string>> = {
  'auth.invalid_credentials': 'E-mail ou senha inválidos.',
  'auth.unauthorized': 'Sua sessão expirou. Entre novamente.',
  network: 'Não foi possível conectar ao servidor. Verifique sua conexão e tente novamente.',
  unknown: 'Não foi possível concluir a operação. Tente novamente.',
};

/** Mensagem específica de indisponibilidade temporária (503) — não é um `ApiErrorCode` porque o backend não anexa um. */
export const SERVICE_UNAVAILABLE_MESSAGE =
  'O serviço está temporariamente indisponível. Tente novamente em instantes.';

/**
 * Mensagem para um recurso de tarefa não encontrado (404) — sem `ApiErrorCode` próprio
 * no backend. No T2 o único 404 possível é de tarefa (`GET /api/tasks/{id}`), daí o texto
 * específico em vez de um genérico "recurso" (FE-03, tabela de mensagens).
 */
export const NOT_FOUND_MESSAGE = 'Tarefa não encontrada.';
