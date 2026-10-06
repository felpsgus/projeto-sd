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

export async function createTask(
  page: Page,
  title: string,
  priority?: string,
  dueDate?: string,
): Promise<void> {
  await page.getByRole('link', { name: 'Nova tarefa' }).first().click();
  // O contador só aparece depois da primeira detecção de mudanças, que é quando o campo é ligado
  // ao formulário. Preencher antes disso (o CI já fez, 1 ms após a troca de rota) perde o valor.
  await expect(page.getByText('0/200', { exact: true })).toBeVisible();
  await page.getByLabel('Título', { exact: true }).fill(title);
  if (priority) {
    await page.getByLabel('Prioridade', { exact: true }).selectOption({ label: priority });
  }
  if (dueDate) await page.getByLabel('Vencimento', { exact: true }).fill(dueDate);
  await page.getByRole('button', { name: 'Criar tarefa' }).click();
  await expect(page.getByRole('heading', { level: 2, name: title })).toBeVisible();
}

export function taskItem(page: Page, title: string) {
  return page.getByRole('listitem').filter({ hasText: title });
}

/** Cadastra e entra sem passar pela tela: o login por API grava o cookie de refresh no contexto
 *  e o `goto` restaura a sessão por ele. Para testes de teclado, em que o setup não é o alvo. */
export async function registerAndLoginViaApi(page: Page): Promise<TestUser> {
  const user = newUser();
  await registerViaApi(page, user);
  const res = await page.request.post('/api/auth/login', {
    data: { email: user.email, password: user.password },
  });
  expect(res.status()).toBe(200);
  await page.goto('/tasks');
  await expect(page.getByRole('heading', { level: 1, name: 'Tarefas' })).toBeVisible();
  return user;
}

/** Console, erros de página e violações de CSP — o que um usuário/DevTools veria de errado. */
/** Respostas de erro esperadas: restaurar sessão sem cookie, credencial errada, tarefa inexistente. */
const EXPECTED_HTTP_ERRORS =
  /^(401 POST \/api\/auth\/(refresh|login)|404 GET \/api\/tasks\/[0-9a-f-]{36})$/;

export function watchProblems(page: Page): string[] {
  const problems: string[] = [];
  // O Chrome registra todo 4xx/5xx de rede como console.error ("Failed to load resource"). Isso
  // não é erro de código: cada resposta >= 400 é conferida abaixo contra a lista esperada.
  page.on('console', (m) => {
    if (m.type() === 'error' && !m.text().startsWith('Failed to load resource')) {
      problems.push(`console.error: ${m.text()}`);
    }
  });
  page.on('response', (r) => {
    const { pathname } = new URL(r.url());
    const line = `${r.status()} ${r.request().method()} ${pathname}`;
    if (r.status() >= 400 && !EXPECTED_HTTP_ERRORS.test(line)) problems.push(`http: ${line}`);
  });
  page.on('pageerror', (e) => problems.push(`pageerror: ${e.message}`));
  void page.exposeFunction('__reportCsp', (v: string) => problems.push(`CSP: ${v}`));
  void page.addInitScript(() => {
    document.addEventListener('securitypolicyviolation', (e) =>
      (window as unknown as { __reportCsp(v: string): void }).__reportCsp(
        `${e.violatedDirective} ${e.blockedURI}`,
      ),
    );
  });
  return problems;
}

export const PUBLIC_PATHS = ['/login', '/register', '/rota-que-nao-existe'];
const UNKNOWN_TASK_PATH = '/tasks/00000000-0000-4000-8000-000000000000/edit';

export async function openScreen(page: Page, path: string): Promise<void> {
  await page.goto(path);
  await expect(page.getByRole('heading', { level: 1 }).first()).toBeVisible();
  if (path === '/tasks')
    await expect(page.getByRole('list', { name: 'Lista de tarefas' })).toBeVisible();
}

/** Cria tarefa pendente, concluída e atrasada; devolve o caminho de edição da pendente. */
export async function seedTasks(page: Page): Promise<string> {
  await createTask(page, 'Tarefa pendente', 'Média');
  await createTask(page, 'Tarefa concluída', 'Baixa');
  await page.getByRole('checkbox', { name: 'Concluir: Tarefa concluída' }).check();
  await expect(page.getByRole('checkbox', { name: 'Reabrir: Tarefa concluída' })).toBeChecked();
  await createTask(page, 'Tarefa atrasada', 'Alta', '2020-01-15');
  await expect(
    taskItem(page, 'Tarefa atrasada').getByText('Atrasada', { exact: true }),
  ).toBeVisible();
  return (await taskItem(page, 'Tarefa pendente')
    .getByRole('link', { name: /Editar/ })
    .getAttribute('href'))!;
}

/**
 * Percorre todas as telas: as públicas sem sessão e, depois de cadastrar, as autenticadas (lista
 * com tarefa pendente, concluída e atrasada, criar, editar, "não encontrada", conta, trocar senha).
 */
export async function forEachScreen(
  page: Page,
  visit: (path: string) => Promise<void>,
): Promise<void> {
  for (const path of PUBLIC_PATHS) {
    await openScreen(page, path);
    await visit(path);
  }
  await registerAndLogin(page);
  const editPath = await seedTasks(page);
  for (const path of [
    '/tasks',
    '/tasks/new',
    editPath,
    UNKNOWN_TASK_PATH,
    '/account',
    '/account/password',
  ]) {
    await openScreen(page, path);
    await visit(path === editPath ? '/tasks/:id/edit' : path);
  }
}

/** Problemas de layout na tela atual: rolagem horizontal, elemento além da borda, conteúdo cortado. */
export function layoutProblems(page: Page): Promise<string[]> {
  return page.evaluate(() => {
    const out: string[] = [];
    const root = document.documentElement;
    if (root.scrollWidth > root.clientWidth) {
      out.push(`rolagem horizontal (${root.scrollWidth} > ${root.clientWidth})`);
    }
    const name = (el: Element) =>
      `<${el.tagName.toLowerCase()}.${el.className}> "${(el.textContent ?? '').trim().slice(0, 30)}"`;
    for (const el of Array.from(document.body.querySelectorAll('*'))) {
      if (!(el as HTMLElement).checkVisibility()) continue;
      const r = el.getBoundingClientRect();
      if (r.width <= 1 || r.height <= 1) continue; // .visually-hidden
      if (r.right > root.clientWidth + 1 || r.left < -1) out.push(`fora da janela: ${name(el)}`);
      const cs = getComputedStyle(el);
      if (el instanceof HTMLInputElement || el instanceof HTMLTextAreaElement) continue; // texto rola por dentro
      const clips =
        ['hidden', 'clip'].includes(cs.overflowX) || ['hidden', 'clip'].includes(cs.overflowY);
      if (clips && (el.scrollWidth > el.clientWidth + 1 || el.scrollHeight > el.clientHeight + 1)) {
        out.push(`conteúdo cortado: ${name(el)}`);
      }
    }
    return out;
  });
}
