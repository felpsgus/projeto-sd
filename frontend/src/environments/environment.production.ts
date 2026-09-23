/**
 * Ambiente de produção.
 *
 * Mesma origem via nginx (FD-16, BE-42): `apiBaseUrl` continua vazio e todo
 * caminho é relativo (`/api/...`). Nenhum segredo ou URL de backend com
 * credencial é versionado aqui.
 */
export const environment = {
  production: true,
  apiBaseUrl: '',
  showHttpStatusIndicator: false,
};
