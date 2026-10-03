import { expect, test, type Page } from '@playwright/test';

import { NEW_TEST_PASSWORD, createTask, newUser } from './support';

/**
 * FE-23 CA-15 (RN-AUTH-05, RN-AUTH-20): em cadastro, login, troca de senha e exclusão de conta,
 * nem a senha nem o access token (nem o valor do cookie de refresh) aparecem em localStorage,
 * sessionStorage, URL (navegações e requisições), console ou DOM serializado. O refresh token
 * só pode existir como cookie HttpOnly.
 */
test('senha e tokens nunca vazam para storage, URL, console ou DOM', async ({ page, context }) => {
  const user = newUser();
  const secrets = new Map<string, string>([
    ['senha', user.password],
    ['nova senha', NEW_TEST_PASSWORD],
  ]);
  const urls: string[] = [];
  const consoleLines: string[] = [];

  page.on('framenavigated', (frame) => urls.push(frame.url()));
  page.on('request', (request) => urls.push(request.url()));
  page.on('console', (message) => consoleLines.push(message.text()));
  page.on('pageerror', (error) => consoleLines.push(error.message));
  page.on('response', async (response) => {
    if (!/\/api\/auth\/(login|refresh)$/.test(response.url()) || response.status() !== 200) return;
    const body = (await response.json()) as { accessToken?: string };
    if (body.accessToken) secrets.set(`access token ${secrets.size}`, body.accessToken);
  });

  async function assertNoLeak(step: string): Promise<void> {
    for (const cookie of await context.cookies()) {
      if (cookie.name === 'refreshToken') {
        expect(cookie.httpOnly, `${step}: refreshToken deve ser HttpOnly`).toBe(true);
        secrets.set('refresh token', cookie.value);
      }
    }
    const storage = await page.evaluate(() =>
      JSON.stringify([
        Object.entries(localStorage),
        Object.entries(sessionStorage),
        document.cookie,
      ]),
    );
    const surfaces: Record<string, string> = {
      storage,
      url: urls.join('\n'),
      console: consoleLines.join('\n'),
      dom: await page.content(),
    };
    for (const [secretName, secret] of secrets) {
      for (const [surface, text] of Object.entries(surfaces)) {
        expect(text.includes(secret), `${step}: ${secretName} vazou em ${surface}`).toBe(false);
      }
    }
  }

  // Cadastro
  await page.goto('/register');
  await page.getByLabel('E-mail', { exact: true }).fill(user.email);
  await page.getByLabel('Senha', { exact: true }).fill(user.password);
  await page.getByLabel('Confirmar senha', { exact: true }).fill(user.password);
  await page.getByRole('button', { name: 'Criar conta' }).click();
  await expect(page.getByText('Conta criada com sucesso.')).toBeVisible();
  await assertNoLeak('cadastro');

  // Login
  await page.getByLabel('Senha', { exact: true }).fill(user.password);
  await page.getByRole('button', { name: 'Entrar' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Tarefas' })).toBeVisible();
  await createTask(page, 'Tarefa para exercitar o token');
  await assertNoLeak('login');

  // Recarregar restaura a sessão pelo cookie (refresh) e emite outro access token
  await page.reload();
  await expect(
    page.getByRole('heading', { level: 2, name: 'Tarefa para exercitar o token' }),
  ).toBeVisible();
  await assertNoLeak('refresh');

  // Troca de senha (campos visíveis também: o atributo `value` nunca pode refletir a senha)
  await page.goto('/account/password');
  await page.getByLabel('Senha atual', { exact: true }).fill(user.password);
  await page.getByLabel('Nova senha', { exact: true }).fill(NEW_TEST_PASSWORD);
  await page.getByLabel('Confirmar nova senha', { exact: true }).fill(NEW_TEST_PASSWORD);
  await page.getByRole('button', { name: 'Mostrar senha' }).first().click();
  await assertNoLeak('troca de senha (preenchida)');
  await page.getByRole('button', { name: 'Alterar senha' }).click();
  await expect(page.getByText('Sua senha foi alterada.')).toBeVisible();
  await assertNoLeak('troca de senha (concluída)');

  // Login com a nova senha, depois exclusão de conta
  await page.getByLabel('Senha', { exact: true }).fill(NEW_TEST_PASSWORD);
  await page.getByRole('button', { name: 'Entrar' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Tarefas' })).toBeVisible();
  await page.goto('/account');
  await openDeleteDialogAndConfirm(page, NEW_TEST_PASSWORD);
  await expect(page.getByText('Sua conta foi excluída.')).toBeVisible();
  await assertNoLeak('exclusão de conta');
});

async function openDeleteDialogAndConfirm(page: Page, password: string): Promise<void> {
  await page.getByRole('button', { name: 'Excluir minha conta' }).click();
  const dialog = page.getByRole('alertdialog', { name: 'Excluir sua conta' });
  await dialog.getByLabel('Confirme sua senha').fill(password);
  await dialog.getByRole('button', { name: 'Excluir permanentemente' }).click();
}
