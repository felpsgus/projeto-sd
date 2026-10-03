import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Locator, type Page } from '@playwright/test';

import { createTask, newUser, registerAndLogin, registerViaApi, taskItem } from './support';

const SKIP_LINK = 'Pular para o conteúdo';

async function expectNoSeriousViolations(page: Page, screen: string): Promise<void> {
  const { violations } = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
    .analyze();
  const blocking = violations
    .filter((v) => v.impact === 'critical' || v.impact === 'serious')
    .map((v) => `${v.id} (${v.impact}): ${v.nodes.map((n) => n.target.join(' ')).join(' | ')}`);
  expect(blocking, `axe em "${screen}"`).toEqual([]);
}

/** Navega só com Tab até o alvo ficar focado — falha se não chegar (ordem/armadilha de foco). */
async function tabTo(page: Page, target: Locator): Promise<void> {
  for (let i = 0; i < 40; i++) {
    if (await target.evaluate((el) => el === document.activeElement)) return;
    await page.keyboard.press('Tab');
  }
  throw new Error(`Tab não alcançou o alvo em 40 passos: ${target}`);
}

test.describe('axe-core em todas as telas (FE-21 CA-01, FE-22 CA-16)', () => {
  test('telas públicas: cadastro, login e 404', async ({ page }) => {
    await page.goto('/register');
    await expect(page.getByRole('heading', { level: 1, name: 'Criar conta' })).toBeVisible();
    await expectNoSeriousViolations(page, 'cadastro');

    await page.goto('/login');
    await expect(page.getByRole('heading', { level: 1, name: 'Entrar' })).toBeVisible();
    await expectNoSeriousViolations(page, 'login');

    await page.goto('/rota-que-nao-existe');
    await expect(
      page.getByRole('heading', { level: 1, name: 'Página não encontrada' }),
    ).toBeVisible();
    await expectNoSeriousViolations(page, '404');
  });

  test('telas autenticadas: lista vazia e com itens, criar, editar, conta, senha, não encontrada', async ({
    page,
  }) => {
    await registerAndLogin(page);
    await expect(page.getByText('Você ainda não tem tarefas.')).toBeVisible();
    await expectNoSeriousViolations(page, 'lista vazia');

    await page.getByRole('link', { name: 'Nova tarefa' }).click();
    await expect(page.getByRole('heading', { level: 1, name: 'Nova tarefa' })).toBeVisible();
    await expectNoSeriousViolations(page, 'criar tarefa');
    await page.getByRole('button', { name: 'Cancelar' }).click();

    await createTask(page, 'Tarefa de auditoria', 'Alta');
    await expectNoSeriousViolations(page, 'lista com itens');

    await taskItem(page, 'Tarefa de auditoria').getByRole('link', { name: 'Editar' }).click();
    await expect(page.getByRole('heading', { level: 1, name: 'Editar tarefa' })).toBeVisible();
    await expectNoSeriousViolations(page, 'editar tarefa');

    await page.goto('/tasks/00000000-0000-4000-8000-000000000000/edit');
    await expect(
      page.getByRole('heading', { level: 1, name: 'Tarefa não encontrada' }),
    ).toBeVisible();
    await expectNoSeriousViolations(page, 'tarefa não encontrada');

    await page.goto('/account');
    await expect(page.getByRole('heading', { level: 1, name: 'Minha conta' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Excluir minha conta' })).toBeVisible();
    await expectNoSeriousViolations(page, 'conta');

    await page.goto('/account/password');
    await expect(page.getByRole('heading', { level: 1, name: 'Alterar senha' })).toBeVisible();
    await expectNoSeriousViolations(page, 'trocar senha');
  });

  test('diálogo de confirmação aberto', async ({ page }) => {
    await registerAndLogin(page);
    await createTask(page, 'Tarefa do diálogo');
    await page.getByRole('button', { name: 'Remover: Tarefa do diálogo' }).click();
    await expect(page.getByRole('alertdialog', { name: 'Remover tarefa' })).toBeVisible();
    await expectNoSeriousViolations(page, 'diálogo de remoção');
  });
});

test('skip link é o primeiro focável e leva o foco ao conteúdo principal (CA-09)', async ({
  page,
}) => {
  await page.goto('/login');
  await expect(page.getByRole('heading', { level: 1, name: 'Entrar' })).toBeVisible();
  await page.keyboard.press('Tab');
  await expect(page.getByRole('link', { name: SKIP_LINK })).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(page.getByRole('main')).toBeFocused();
  await expect(page).toHaveURL(/\/login/);
});

test('mudança de rota leva o foco ao <h1> da nova página (CA-10)', async ({ page }) => {
  await page.goto('/login');
  await page.getByRole('link', { name: 'Cadastre-se' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Criar conta' })).toBeFocused();

  await registerAndLogin(page);
  await page.getByRole('link', { name: 'Nova tarefa' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Nova tarefa' })).toBeFocused();
});

test('diálogo prende o foco, fecha com Esc e devolve o foco (CA-08)', async ({ page }) => {
  await registerAndLogin(page);
  await createTask(page, 'Tarefa do foco');
  const opener = page.getByRole('button', { name: 'Remover: Tarefa do foco' });
  await opener.focus();
  await page.keyboard.press('Enter');

  const dialog = page.getByRole('alertdialog', { name: 'Remover tarefa' });
  await expect(dialog).toBeVisible();
  await expect(dialog.getByRole('button', { name: 'Cancelar' })).toBeFocused();

  // Tab e Shift+Tab nunca saem do diálogo
  for (let i = 0; i < 4; i++) {
    await page.keyboard.press('Tab');
    expect(await dialog.evaluate((el) => el.contains(document.activeElement))).toBe(true);
  }
  await page.keyboard.press('Shift+Tab');
  expect(await dialog.evaluate((el) => el.contains(document.activeElement))).toBe(true);

  await page.keyboard.press('Escape');
  await expect(dialog).toBeHidden();
  await expect(opener).toBeFocused();
});

test('fluxo completo só por teclado: login -> criar tarefa -> concluir (CA-04 parcial)', async ({
  page,
}) => {
  const user = newUser();
  await registerViaApi(page, user);
  await page.goto('/login');

  await tabTo(page, page.getByLabel('E-mail', { exact: true }));
  await page.keyboard.type(user.email);
  await page.keyboard.press('Tab');
  await page.keyboard.type(user.password);
  await page.keyboard.press('Enter');
  await expect(page.getByRole('heading', { level: 1, name: 'Tarefas' })).toBeVisible();

  await tabTo(page, page.getByRole('link', { name: 'Nova tarefa' }));
  await page.keyboard.press('Enter');
  await expect(page.getByRole('heading', { level: 1, name: 'Nova tarefa' })).toBeVisible();
  await tabTo(page, page.getByLabel('Título', { exact: true }));
  await page.keyboard.type('Tarefa só por teclado');
  await tabTo(page, page.getByRole('button', { name: 'Criar tarefa' }));
  await page.keyboard.press('Enter');
  await expect(
    page.getByRole('heading', { level: 2, name: 'Tarefa só por teclado' }),
  ).toBeVisible();

  const toggle = page.getByRole('checkbox', { name: 'Concluir: Tarefa só por teclado' });
  await tabTo(page, toggle);
  await page.keyboard.press('Space');
  await expect(
    page.getByRole('checkbox', { name: 'Reabrir: Tarefa só por teclado' }),
  ).toBeChecked();
});

test.describe('360 px (CA-18, CA-19)', () => {
  test.use({ viewport: { width: 360, height: 740 } });

  test('sem rolagem horizontal nas telas principais', async ({ page }) => {
    const noHorizontalScroll = () =>
      page.evaluate(
        () => document.documentElement.scrollWidth <= document.documentElement.clientWidth,
      );

    for (const path of ['/login', '/register']) {
      await page.goto(path);
      await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
      expect(await noHorizontalScroll(), path).toBe(true);
    }

    await registerAndLogin(page);
    await createTask(
      page,
      'Uma tarefa com um título consideravelmente longo para forçar quebra de linha',
    );
    expect(await noHorizontalScroll(), '/tasks').toBe(true);
    for (const path of ['/tasks/new', '/account', '/account/password']) {
      await page.goto(path);
      await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
      expect(await noHorizontalScroll(), path).toBe(true);
    }
  });

  test('botões e links de ação têm alvo de toque de ao menos 44 px', async ({ page }) => {
    await registerAndLogin(page);
    await createTask(page, 'Tarefa para medir alvos');
    const small: string[] = [];
    for (const path of ['/tasks', '/tasks/new', '/account', '/account/password']) {
      await page.goto(path);
      await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
      for (const role of ['button', 'checkbox', 'radio'] as const) {
        for (const el of await page.getByRole(role).all()) {
          if (!(await el.isVisible())) continue;
          // checkbox/radio: o alvo é o <label> que o envolve (quando existe)
          const target = role === 'button' ? el : el.locator('xpath=ancestor-or-self::label[1]');
          const box = (await target.count()) ? await target.boundingBox() : await el.boundingBox();
          if (box && (box.height < 44 || box.width < 44)) {
            small.push(
              `${path} ${role} "${(await el.getAttribute('aria-label')) ?? (await el.innerText().catch(() => ''))}" ${Math.round(box.width)}x${Math.round(box.height)}`,
            );
          }
        }
      }
    }
    expect(small).toEqual([]);
  });
});
