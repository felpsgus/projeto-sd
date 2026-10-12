import { environment } from '../../../environments/environment';

/**
 * Reconhece se uma URL de requisição é destinada à API (`apiBaseUrl` do environment,
 * hoje vazio no T2 — o que corresponde a qualquer caminho relativo iniciando em `/api`).
 *
 * Existe para que os interceptors (`X-Client-Date`, `Authorization`) não anexem cabeçalho
 * nenhum a requisições para fora da API — um asset externo, por exemplo (FE-02, CA-14;
 * FE-06, CA-03).
 */
export function isApiRequest(url: string): boolean {
  const base = environment.apiBaseUrl;
  if (base) {
    return url.startsWith(base);
  }
  return url.startsWith('/api/') || url === '/api';
}
