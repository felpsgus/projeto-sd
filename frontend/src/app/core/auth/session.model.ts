/**
 * Motivo do encerramento de sessão — cada um produz uma mensagem diferente na tela de
 * login (FE-09, CA-16/CA-17). `user_logout` (FE-10) e `session_expired` (FE-06 — 401 de
 * token ausente/inválido/expirado) continuam sem mensagem de contexto própria diferente
 * do já existente. `session_revoked` passa a ser produzido pela troca de senha (FE-12,
 * RN-AUTH-19) — o texto exibido é parcial (só "você foi desconectado", sem "todos os
 * dispositivos": o backend do T2 não revoga sessões de fato, ver comentário em
 * `ChangePasswordComponent`). `account_deleted` (FE-13) é produzido após a exclusão de
 * conta bem-sucedida.
 */
export type SessionEndReason =
  'user_logout' | 'session_expired' | 'session_revoked' | 'account_deleted';
