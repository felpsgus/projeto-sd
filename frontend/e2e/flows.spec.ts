import { expect, test } from '@playwright/test';

import {
  NEW_TEST_PASSWORD,
  TEST_PASSWORD,
  createTask,
  login,
  newUser,
  registerAndLogin,
  registerViaApi,
  taskItem,
} from './support';

test('1. cadastro -> login -> lista vazia', async ({ page }) => {
  const user = newUser();
  await page.goto('/register');
  await page.getByLabel('E-mail', { exact: true }).fill(user.email);
  await page.getByLabel('Senha', { exact: true }).fill(user.password);
  await page.getByLabel('Confirmar senha', { exact: true }).fill(user.password);
  await page.getByRole('button', { name: 'Criar conta' }).click();
  await expect(page.getByText('Conta criada com sucesso.')).toBeVisible();

  await page.getByLabel('Senha', { exact: true }).fill(user.password);
  await page.getByRole('button', { name: 'Entrar' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Tarefas' })).toBeVisible();
  await expect(page.getByText('Você ainda não tem tarefas.')).toBeVisible();
});

test('2. criar -> aparece -> concluir -> aparece concluída', async ({ page }) => {
  await registerAndLogin(page);
  await createTask(page, 'Pagar a conta de luz');
  await page.getByRole('checkbox', { name: 'Concluir: Pagar a conta de luz' }).check();
  await expect(page.getByRole('checkbox', { name: 'Reabrir: Pagar a conta de luz' })).toBeChecked();
  await expect(taskItem(page, 'Pagar a conta de luz').getByText(/^Concluída em \d/)).toBeVisible();
});

test('3. editar -> alteração persiste após recarregar', async ({ page }) => {
  await registerAndLogin(page);
  await createTask(page, 'Titulo original');
  await taskItem(page, 'Titulo original').getByRole('link', { name: 'Editar' }).click();
  await page.getByLabel('Título', { exact: true }).fill('Titulo editado');
  await page.getByRole('button', { name: 'Salvar alterações' }).click();
  await expect(page.getByRole('heading', { level: 2, name: 'Titulo editado' })).toBeVisible();

  await page.reload();
  await expect(page.getByRole('heading', { level: 2, name: 'Titulo editado' })).toBeVisible();
  await expect(page.getByRole('heading', { level: 2, name: 'Titulo original' })).toHaveCount(0);
});

test('4. remover com confirmação -> some da lista e do recarregamento', async ({ page }) => {
  await registerAndLogin(page);
  await createTask(page, 'Tarefa a remover');
  await page.getByRole('button', { name: 'Remover: Tarefa a remover' }).click();
  const dialog = page.getByRole('alertdialog', { name: 'Remover tarefa' });
  await dialog.getByRole('button', { name: 'Remover' }).click();
  await expect(page.getByRole('heading', { level: 2, name: 'Tarefa a remover' })).toHaveCount(0);
  await page.reload();
  await expect(page.getByText('Você ainda não tem tarefas.')).toBeVisible();
});

test('5. filtrar e buscar -> URL reflete -> F5 preserva a visão', async ({ page }) => {
  await registerAndLogin(page);
  await createTask(page, 'Alfa pendente');
  await createTask(page, 'Beta concluida');
  await page.getByRole('checkbox', { name: 'Concluir: Beta concluida' }).check();
  await expect(page.getByRole('checkbox', { name: 'Reabrir: Beta concluida' })).toBeChecked();

  await page.getByRole('radio', { name: 'Pendentes' }).check();
  await expect(page).toHaveURL(/status=pending/);
  await page.getByRole('searchbox', { name: 'Buscar' }).fill('Alfa');
  await expect(page).toHaveURL(/search=Alfa/);
  await expect(page.getByRole('heading', { level: 2, name: 'Beta concluida' })).toHaveCount(0);

  await page.reload();
  await expect(page.getByRole('radio', { name: 'Pendentes' })).toBeChecked();
  await expect(page.getByRole('searchbox', { name: 'Buscar' })).toHaveValue('Alfa');
  await expect(page.getByRole('heading', { level: 2, name: 'Alfa pendente' })).toBeVisible();
  await expect(page.getByRole('heading', { level: 2, name: 'Beta concluida' })).toHaveCount(0);
});

test('6. senha errada -> mensagem genérica', async ({ page }) => {
  const user = newUser();
  await registerViaApi(page, user);
  await login(page, { ...user, password: 'Senha-errada-999' });
  await expect(page.getByRole('alert')).toHaveText(/E-mail ou senha inválidos\./);
  await expect(page).toHaveURL(/\/login/);
});

test('7. trocar senha -> sessão cai -> login com a nova senha', async ({ page }) => {
  const user = await registerAndLogin(page);
  await page.goto('/account/password');
  await page.getByLabel('Senha atual', { exact: true }).fill(user.password);
  await page.getByLabel('Nova senha', { exact: true }).fill(NEW_TEST_PASSWORD);
  await page.getByLabel('Confirmar nova senha', { exact: true }).fill(NEW_TEST_PASSWORD);
  await page.getByRole('button', { name: 'Alterar senha' }).click();
  await expect(page).toHaveURL(/\/login/);
  await expect(page.getByText('Sua senha foi alterada.')).toBeVisible();

  await page.getByLabel('Senha', { exact: true }).fill(NEW_TEST_PASSWORD);
  await page.getByRole('button', { name: 'Entrar' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Tarefas' })).toBeVisible();
});

test('8. sessão persiste ao recarregar (F5) numa rota autenticada', async ({ page }) => {
  await registerAndLogin(page);
  await page.goto('/account');
  // Espera a sessão restaurar (refresh rotaciona o cookie) antes do F5 — recarregar com o
  // refresh ainda em voo é uma corrida do teste, não um fluxo de usuário.
  await expect(page.getByRole('heading', { level: 1, name: 'Minha conta' })).toBeVisible();
  await page.reload();
  await expect(page.getByRole('heading', { level: 1, name: 'Minha conta' })).toBeVisible();
  await expect(page).toHaveURL(/\/account$/);
});

test('9. expiração do access token -> renovação transparente', async ({ page }) => {
  // Sem esperar 15 min reais: o relógio do navegador salta além do `expiresAt` do token.
  await page.clock.install();
  await page.clock.resume();
  await registerAndLogin(page);

  const visited: string[] = [];
  page.on('framenavigated', (frame) => visited.push(frame.url()));
  const refresh = page.waitForResponse(
    (r) => r.url().includes('/api/auth/refresh') && r.request().method() === 'POST',
  );
  await page.clock.setSystemTime(new Date(Date.now() + 20 * 60_000));

  await createTask(page, 'Criada com token renovado');
  expect((await refresh).status()).toBe(200);
  expect(visited.filter((url) => url.includes('/login'))).toEqual([]);
  await expect(page.getByRole('heading', { level: 1, name: 'Entrar' })).toHaveCount(0);
});

test('10. logout -> /tasks leva ao login; "voltar" não mostra a tela anterior', async ({
  page,
}) => {
  await registerAndLogin(page);
  await page.getByRole('link', { name: 'Nova tarefa' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Nova tarefa' })).toBeVisible();
  await page.getByRole('button', { name: 'Sair' }).click();
  await expect(page).toHaveURL(/\/login/);

  await page.goto('/tasks');
  await expect(page).toHaveURL(/\/login/);

  await page.goBack();
  await expect(page.getByRole('heading', { level: 1, name: 'Tarefas' })).toHaveCount(0);
  await expect(page.getByRole('heading', { level: 1, name: 'Nova tarefa' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Sair' })).toHaveCount(0);
});

test('11. tarefa de outro usuário = id inexistente (mesma tela)', async ({ page, browser }) => {
  await registerAndLogin(page);
  await createTask(page, 'Tarefa do usuário A');
  const href = await taskItem(page, 'Tarefa do usuário A')
    .getByRole('link', { name: 'Editar' })
    .getAttribute('href');
  expect(href).toMatch(/^\/tasks\/[0-9a-f-]{36}\/edit$/);

  const contextB = await browser.newContext();
  try {
    const pageB = await contextB.newPage();
    await registerAndLogin(pageB);

    await pageB.goto(href!);
    const heading = pageB.getByRole('heading', { level: 1, name: 'Tarefa não encontrada' });
    await expect(heading).toBeVisible();
    const foreign = await pageB.getByRole('main').innerText();

    await pageB.goto('/tasks/00000000-0000-4000-8000-000000000000/edit');
    await expect(heading).toBeVisible();
    expect(await pageB.getByRole('main').innerText()).toBe(foreign);
  } finally {
    await contextB.close();
  }
});

test('12. excluir conta -> login com as credenciais antigas falha', async ({ page }) => {
  const user = await registerAndLogin(page);
  await page.goto('/account');
  await page.getByRole('button', { name: 'Excluir minha conta' }).click();
  const dialog = page.getByRole('alertdialog', { name: 'Excluir sua conta' });
  await dialog.getByLabel('Confirme sua senha').fill(TEST_PASSWORD);
  await dialog.getByRole('button', { name: 'Excluir permanentemente' }).click();
  await expect(page).toHaveURL(/\/login/);
  await expect(page.getByText('Sua conta foi excluída.')).toBeVisible();

  await login(page, user);
  await expect(page.getByRole('alert')).toHaveText(/E-mail ou senha inválidos\./);
});
