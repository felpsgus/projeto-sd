import { expect, type Page } from '@playwright/test';

// Constantes óbvias de teste (RN-AUTH-04: >= 8, letra e número) — não são segredo.
export const TEST_PASSWORD = 'Senha-de-teste-123';
export const NEW_TEST_PASSWORD = 'Outra-senha-de-teste-456';

export interface TestUser {
  email: string;
  password: string;
}

/** Cada teste cria o próprio usuário: e-mail único por UUID, sem estado compartilhado. */
export function newUser(): TestUser {
  return { email: `e2e-${crypto.randomUUID()}@example.test`, password: TEST_PASSWORD };
}

/** Cadastra pela API (rápido) — o cadastro pela tela é o fluxo 1. */
export async function registerViaApi(page: Page, user: TestUser): Promise<void> {
  const res = await page.request.post('/api/auth/register', {
    data: { email: user.email, password: user.password },
  });
  expect(res.status()).toBe(201);
}

export async function login(page: Page, user: TestUser): Promise<void> {
  await page.goto('/login');
  await page.getByLabel('E-mail', { exact: true }).fill(user.email);
  await page.getByLabel('Senha', { exact: true }).fill(user.password);
  await page.getByRole('button', { name: 'Entrar' }).click();
}

export async function registerAndLogin(page: Page): Promise<TestUser> {
  const user = newUser();
  await registerViaApi(page, user);
  await login(page, user);
  await expect(page.getByRole('heading', { level: 1, name: 'Tarefas' })).toBeVisible();
  return user;
}

export async function createTask(page: Page, title: string, priority?: string): Promise<void> {
  await page.getByRole('link', { name: 'Nova tarefa' }).first().click();
  await page.getByLabel('Título', { exact: true }).fill(title);
  if (priority) {
    await page.getByLabel('Prioridade', { exact: true }).selectOption({ label: priority });
  }
  await page.getByRole('button', { name: 'Criar tarefa' }).click();
  await expect(page.getByRole('heading', { level: 2, name: title })).toBeVisible();
}

export function taskItem(page: Page, title: string) {
  return page.getByRole('listitem').filter({ hasText: title });
}
