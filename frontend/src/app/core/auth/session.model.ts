/**
 * Motivo do encerramento de sessão — cada um produz uma mensagem diferente na tela de
 * login (FE-05, CA-16): `user_logout` (FE-10) mostra só uma confirmação discreta;
 * `session_expired` (FE-06 — refresh recusado) "sua sessão expirou"; `session_revoked`
 * (revogação no servidor) "sua sessão foi encerrada"; `password_changed` (FE-12 — o
 * backend revoga todas as sessões) "senha alterada, entre novamente"; `account_deleted`
 * (FE-13) após a exclusão de conta.
 */
export type SessionEndReason =
  'user_logout' | 'session_expired' | 'session_revoked' | 'password_changed' | 'account_deleted';

/** `unknown` só existe durante o bootstrap, até a tentativa de restaurar a sessão terminar (FE-05). */
export type SessionStatus = 'unknown' | 'authenticated' | 'anonymous';
