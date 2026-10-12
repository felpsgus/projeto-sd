import { defineConfig, devices } from '@playwright/test';

// Sem @types/node no projeto (nenhuma dependência além do Playwright): só o que usamos.
declare const process: { env: Record<string, string | undefined> };

// A suíte roda contra a stack real (compose, perfil `full`): nginx + build de produção + backend.
// Endereço vem de variável de ambiente — nunca literal nos testes.
export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  retries: 0,
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL: process.env['E2E_BASE_URL'] ?? 'http://localhost',
    trace: 'retain-on-failure',
    video: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
    {
      // CA-17: viewport de 360 px (Chromium, touch + mobile) — só os specs de fluxo/layout.
      name: 'mobile-360',
      use: { ...devices['Pixel 5'], viewport: { width: 360, height: 740 } },
      testMatch: /(flows|a11y)\.spec\.ts/,
    },
    // Fora do comando padrão de PR (`npm run e2e:all`).
    { name: 'firefox', use: { ...devices['Desktop Firefox'] } },
    { name: 'webkit', use: { ...devices['Desktop Safari'] } },
  ],
});
