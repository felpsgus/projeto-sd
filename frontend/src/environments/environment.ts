/**
 * Ambiente de desenvolvimento.
 *
 * `apiBaseUrl` fica vazio de propósito: todas as chamadas usam caminhos relativos
 * (`/api/...`), nunca uma URL absoluta do Gateway (FD-16). Em `ng serve`, o
 * `proxy.conf.json` encaminha `/api` para `http://localhost:8080`, reproduzindo a
 * mesma origem que o nginx terá em produção (BE-42).
 */
export const environment = {
  production: false,
  apiBaseUrl: '',
  /** Renova o access token quando ele expira em menos de N segundos (FE-06, FD-13). */
  refreshSkewSeconds: 30,
  /** Mostra, num canto discreto da tela, o método/rota/status da última chamada HTTP (FE-03, recorte T2). */
  showHttpStatusIndicator: true,
};
