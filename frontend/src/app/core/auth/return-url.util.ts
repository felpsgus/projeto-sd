/** Rota padrão para quem não veio de um redirecionamento (FE-07, CA-16). */
export const DEFAULT_AUTHENTICATED_ROUTE = '/tasks';

/**
 * Aceita `url` como `returnUrl` só se for um caminho **interno e relativo** da própria
 * aplicação — nunca um host externo. É a defesa contra open redirect (FE-07, CA-09):
 * `?returnUrl=https://site-malicioso/`, `//site-malicioso/` e `javascript:alert(1)`
 * são todas formas de escapar da aplicação que esta função rejeita.
 */
export function buildSafeReturnUrl(url: string): string {
  if (url.startsWith('/') && !url.startsWith('//')) {
    return url;
  }
  return DEFAULT_AUTHENTICATED_ROUTE;
}

/** Usado após o login: idêntico a {@link buildSafeReturnUrl}, mas aceita `null`/`undefined` do query param. */
export function resolveReturnUrl(returnUrl: string | null | undefined): string {
  if (!returnUrl) {
    return DEFAULT_AUTHENTICATED_ROUTE;
  }
  return buildSafeReturnUrl(returnUrl);
}
