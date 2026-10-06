import { AppErrorCode } from './app-error.model';

/**
 * Mapa central de código de erro → mensagem pt-BR (FD-03; FE-03, CA-11). Nenhuma tela
 * escreve uma mensagem de erro fora daqui — inclusive a mensagem genérica de fallback.
 *
 * Catálogo restrito ao que o backend emite e o frontend trata. Um código novo
 * do backend cai automaticamente na mensagem genérica até ser adicionado aqui — nunca é
 * exibido cru ao usuário (CA-03).
 */
export const ERROR_MESSAGES: Readonly<Record<AppErrorCode, string>> = {
  'auth.invalid_credentials': 'E-mail ou senha inválidos.',
  'auth.unauthorized': 'Sua sessão expirou. Entre novamente.',
  // FE-08 (409 no cadastro) — a tela mostra este texto junto ao campo de e-mail, com link
  // para o login (RN-AUTH-02 permite apontar duplicidade no cadastro, diferente de login).
  'auth.email_already_registered': 'Este e-mail já está cadastrado.',
  // FE-12/FE-13 (400 com `errors.currentPassword`/`errors.password`) — usado como
  // mensagem de reserva; a tela sempre prioriza o texto específico do campo (`fieldErrors`).
  'auth.invalid_current_password': 'Senha atual incorreta.',
  // FE-09 (429, RN-AUTH-13): sem `Retry-After` cai neste texto; com ele, ver `tooManyAttemptsMessage`.
  'auth.too_many_attempts': 'Muitas tentativas. Tente novamente em alguns minutos.',
  // FE-06 CA-12 (401 do refresh, RN-AUTH-19): lido pelo SessionRefresher; a tela de login usa o motivo `session_revoked`.
  'auth.refresh_token_revoked': 'Sua sessão foi encerrada. Entre novamente.',
  // FE-19 (409, RN-TASK-06): o estado mudou em outro lugar — nunca uma mensagem genérica
  // de falha, porque a ação em si não falhou por engano do usuário.
  'task.already_completed': 'Esta tarefa já foi concluída em outro lugar.',
  'task.not_completed': 'Esta tarefa não está mais concluída — foi reaberta em outro lugar.',
  // FE-19 (reabrir) e FE-17 (criar): mesmo código, RN-TASK-15.
  'task.active_limit_reached':
    'Você atingiu o limite de tarefas pendentes. Conclua ou remova alguma tarefa antes de continuar.',
  network: 'Não foi possível conectar ao servidor. Verifique sua conexão e tente novamente.',
  unknown: 'Não foi possível concluir a operação. Tente novamente.',
};

/** Mensagem de 429 do login: converte `Retry-After` (segundos) em minutos, arredondando para cima. */
export function tooManyAttemptsMessage(retryAfterSeconds?: number): string {
  if (retryAfterSeconds === undefined) {
    return ERROR_MESSAGES['auth.too_many_attempts'];
  }
  const minutes = Math.max(1, Math.ceil(retryAfterSeconds / 60));
  return `Muitas tentativas. Tente novamente em ${minutes} ${minutes === 1 ? 'minuto' : 'minutos'}.`;
}

/** Mensagem do 409 de limite de tarefas pendentes (RN-TASK-15); com o número do limite quando conhecido. */
export function activeLimitMessage(limit?: number): string {
  if (limit === undefined) {
    return ERROR_MESSAGES['task.active_limit_reached'];
  }
  return `Você atingiu o limite de ${limit} tarefas pendentes. Conclua ou remova alguma tarefa antes de continuar.`;
}

/** Mensagem específica de indisponibilidade temporária (503) — não é um `ApiErrorCode` porque o backend não anexa um. */
export const SERVICE_UNAVAILABLE_MESSAGE =
  'O serviço está temporariamente indisponível. Tente novamente em instantes.';

/**
 * Mensagem para um recurso de tarefa não encontrado (404) — sem `ApiErrorCode` próprio
 * no backend. No T2 o único 404 possível é de tarefa (`GET /api/tasks/{id}`), daí o texto
 * específico em vez de um genérico "recurso" (FE-03, tabela de mensagens).
 */
export const NOT_FOUND_MESSAGE = 'Tarefa não encontrada.';

/** FE-07 CA-13: o chunk de uma rota lazy não baixou (rede caiu); a tela oferece tentar de novo. */
export const ROUTE_LOAD_ERROR_MESSAGE =
  'Não foi possível carregar esta página. Verifique sua conexão e tente novamente.';

/** 400 com `errors` por campo: o texto de cada campo fica junto dele; este é o resumo geral (FE-03). */
export const VALIDATION_SUMMARY_MESSAGE = 'Verifique os campos destacados.';

/** Falha ao carregar o perfil em "Minha conta" (FE-11), quando o erro não traz mensagem própria. */
export const PROFILE_LOAD_ERROR_MESSAGE = 'Não foi possível carregar seu perfil.';

/** Falha ao carregar a Tarefa na edição (FE-18); a tela oferece tentar de novo. */
export const TASK_LOAD_ERROR_MESSAGE = 'Não foi possível carregar a tarefa.';
