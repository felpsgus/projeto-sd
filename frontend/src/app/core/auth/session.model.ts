/**
 * Motivo do encerramento de sessão — cada um produz uma mensagem diferente na tela de
 * login (FE-09, CA-16/CA-17). No recorte do T2 só `user_logout` (FE-10) e
 * `session_expired` (FE-06 — 401 de token ausente/inválido/expirado) são produzidos;
 * `session_revoked` fica documentado para quando FE-06 integral existir.
 */
export type SessionEndReason = 'user_logout' | 'session_expired' | 'session_revoked';
