import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Locator, type Page } from '@playwright/test';

import {
  NEW_TEST_PASSWORD,
  TEST_PASSWORD,
  createTask,
  forEachScreen,
  layoutProblems,
  newUser,
  openScreen,
  registerAndLogin,
  registerAndLoginViaApi,
  registerViaApi,
  taskItem,
  watchProblems,
} from './support';

const SKIP_LINK = 'Pular para o conteúdo';

// Varreduras por tela cadastram, criam tarefas e percorrem ~9 telas: o padrão de 30 s não basta.
test.setTimeout(90_000);

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
  for (let i = 0; i < 80; i++) {
    if (await target.evaluate((el) => el === document.activeElement)) return;
    await page.keyboard.press('Tab');
  }
  throw new Error(`Tab não alcançou o alvo em 80 passos: ${target}`);
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
});

// ---------------------------------------------------------------------------------------------
// Varreduras por tela (FE-01 CA-03, FE-04 CA-04/06/11, FE-15 CA-04/10, FE-21 CA-05/06/07/16/17/19/20/21)
// Todas usam `forEachScreen`: telas públicas sem sessão + autenticadas com tarefa pendente,
// concluída e atrasada.
// ---------------------------------------------------------------------------------------------

test('console limpo, sem pageerror e sem violação de CSP ao navegar pelas telas (FE-01 CA-03)', async ({
  page,
}) => {
  const problems = watchProblems(page);
  await forEachScreen(page, () => Promise.resolve());
  // também as telas com estado de erro: login inválido
  await page.getByRole('button', { name: 'Sair' }).click();
  await page.getByLabel('E-mail', { exact: true }).fill(newUser().email);
  await page.getByLabel('Senha', { exact: true }).fill(TEST_PASSWORD);
  await page.getByRole('button', { name: 'Entrar' }).click();
  await expect(page.getByRole('alert')).toBeVisible();
  expect(problems).toEqual([]);
});

for (const width of [360, 768, 1440, 1920]) {
  test(`layout a ${width} px: sem rolagem horizontal, sem corte e coluna dentro de --content-max-width`, async ({
    page,
  }) => {
    await page.setViewportSize({ width, height: 900 });
    const problems: string[] = [];
    let columns = 0;
    await forEachScreen(page, async (path) => {
      problems.push(...(await layoutProblems(page)).map((p) => `${path}: ${p}`));
      const col = await page.evaluate(() => {
        const column = document.querySelector('.app-shell__content, .auth-layout__card');
        if (!column) return null;
        const probe = document.createElement('div');
        probe.style.width = 'var(--content-max-width)';
        document.body.append(probe);
        const max = probe.getBoundingClientRect().width;
        probe.remove();
        return { width: column.getBoundingClientRect().width, max };
      });
      if (col) {
        columns++;
        if (col.width > col.max + 1) problems.push(`${path}: coluna ${col.width}px > ${col.max}px`);
      }
    });
    expect(columns).toBeGreaterThan(5);
    expect(problems).toEqual([]);
  });
}

for (const scheme of ['light', 'dark'] as const) {
  test(`contraste (axe) no tema ${scheme}: telas, concluída, atrasada, erro e diálogo`, async ({
    page,
  }) => {
    await page.emulateMedia({ colorScheme: scheme });
    const failures: string[] = [];
    const check = async (label: string) => {
      const { violations } = await new AxeBuilder({ page }).withRules(['color-contrast']).analyze();
      for (const v of violations) {
        failures.push(
          `${label}: ${v.nodes.map((n) => `${n.target.join(' ')} ${n.any[0]?.message}`).join(' | ')}`,
        );
      }
    };

    // estado de erro de formulário visível + botão desabilitado (cadastro com dados inválidos)
    await openScreen(page, '/register');
    await page.getByLabel('E-mail', { exact: true }).fill('invalido');
    await page.getByLabel('Senha', { exact: true }).fill('curta');
    await page.getByLabel('Confirmar senha', { exact: true }).fill('outra');
    await page.getByLabel('Nome de exibição (opcional)').focus();
    await expect(page.locator('.form-field-error').first()).toBeVisible();
    await expect(page.getByRole('button', { name: 'Criar conta' })).toBeDisabled();
    await check('cadastro com erros e botão desabilitado');

    await openScreen(page, '/login');
    await page.getByRole('button', { name: 'Entrar' }).click();
    await expect(page.locator('.login__hint').first()).toBeVisible();
    await check('login com dicas de erro');

    await forEachScreen(page, (path) => check(path));

    await page.goto('/account/password');
    await page.getByRole('button', { name: 'Alterar senha' }).click();
    await expect(page.locator('.form-field-error').first()).toBeVisible();
    await check('trocar senha com erros');

    await openScreen(page, '/tasks');
    await page.getByRole('button', { name: 'Remover: Tarefa pendente' }).click();
    await expect(page.getByRole('alertdialog', { name: 'Remover tarefa' })).toBeVisible();
    await check('diálogo de remoção');

    expect(failures).toEqual([]);
  });
}

test('título de 200 caracteres (com e sem espaços) não gera rolagem nem extrapola o item (FE-15 CA-04)', async ({
  page,
}) => {
  await registerAndLogin(page);
  const titles = [
    'A'.repeat(200),
    Array.from({ length: 30 }, (_, i) => `palavra${i}`)
      .join(' ')
      .slice(0, 200)
      .trimEnd(),
  ];
  for (const title of titles) await createTask(page, title);

  for (const width of [360, 1280]) {
    await page.setViewportSize({ width, height: 900 });
    await openScreen(page, '/tasks');
    expect(await layoutProblems(page), `${width}px`).toEqual([]);
    const overflowing = await page.evaluate(
      () =>
        Array.from(document.querySelectorAll('app-task-item')).filter((item) => {
          const box = item.getBoundingClientRect();
          return Array.from(item.querySelectorAll('*')).some(
            (el) => el.getBoundingClientRect().right > box.right + 1,
          );
        }).length,
    );
    expect(overflowing, `${width}px: itens com conteúdo além da própria borda`).toBe(0);
  }
});

test.describe('360 px: todos os alvos interativos (FE-21 CA-19)', () => {
  test.use({ viewport: { width: 360, height: 740 } });

  test('botões, links, campos e checkbox/radio (pelo label) têm >= 44 px; links em linha são medidos e isentos', async ({
    page,
  }) => {
    const small: string[] = [];
    const exempt: string[] = [];
    await forEachScreen(page, async (path) => {
      const found = await page.evaluate(() => {
        const rows: { name: string; w: number; h: number; inline: boolean }[] = [];
        const selector =
          'a[href], button, input:not([type=hidden]), select, textarea, summary, [role=button]';
        for (const el of Array.from(document.querySelectorAll<HTMLElement>(selector))) {
          if (!el.checkVisibility() || el.classList.contains('skip-link')) continue;
          const isCheck = el instanceof HTMLInputElement && ['checkbox', 'radio'].includes(el.type);
          const target = isCheck ? (el.closest('label') ?? el) : el;
          const r = target.getBoundingClientRect();
          if (r.width <= 1 || r.height <= 1) continue;
          // link em linha dentro de frase: o texto do pai fora do link não é vazio
          const parent = el.parentElement;
          const inline =
            el.tagName === 'A' &&
            getComputedStyle(el).display === 'inline' &&
            !!parent &&
            (parent.textContent ?? '').replace(el.textContent ?? '', '').trim().length > 0;
          rows.push({
            name: `${el.tagName.toLowerCase()} "${el.getAttribute('aria-label') ?? (el.textContent ?? '').trim().slice(0, 30)}"`,
            w: Math.round(r.width),
            h: Math.round(r.height),
            inline,
          });
        }
        return rows;
      });
      for (const f of found) {
        if (f.w >= 44 && f.h >= 44) continue;
        (f.inline ? exempt : small).push(`${path} ${f.name} ${f.w}x${f.h}`);
      }
    });
    // o skip link só aparece com foco: mede focado
    await page.goto('/login');
    await page.keyboard.press('Tab');
    const skip = await page.getByRole('link', { name: SKIP_LINK }).boundingBox();
    if (skip && (skip.width < 44 || skip.height < 44)) {
      small.push(`skip link ${Math.round(skip.width)}x${Math.round(skip.height)}`);
    }
    console.log(`links em linha isentos (WCAG 2.5.8) e menores que 44 px:\n${exempt.join('\n')}`);
    expect(small).toEqual([]);
  });
});

test('zoom de texto a 200%: sem corte, sem rolagem horizontal e ações principais acionáveis (FE-21 CA-21)', async ({
  page,
}) => {
  const link = (p: Page, name: string) => p.getByRole('link', { name });
  const button = (p: Page, name: string) => p.getByRole('button', { name, exact: true });
  const primary: Record<string, (p: Page) => Locator[]> = {
    '/login': (p) => [button(p, 'Entrar'), link(p, 'Cadastre-se')],
    '/register': (p) => [button(p, 'Criar conta')],
    '/rota-que-nao-existe': (p) => [link(p, 'Voltar ao início')],
    '/tasks': (p) => [
      link(p, 'Nova tarefa'),
      link(p, 'Editar: Tarefa pendente'),
      button(p, 'Remover: Tarefa pendente'),
      button(p, 'Sair'),
    ],
    '/tasks/new': (p) => [button(p, 'Criar tarefa'), button(p, 'Cancelar')],
    '/tasks/:id/edit': (p) => [button(p, 'Salvar alterações'), button(p, 'Cancelar')],
    '/tasks/00000000-0000-4000-8000-000000000000/edit': (p) => [link(p, 'Voltar para a lista')],
    '/account': (p) => [
      button(p, 'Salvar'),
      p.locator('main').getByRole('button', { name: 'Sair', exact: true }),
      button(p, 'Excluir minha conta'),
    ],
    '/account/password': (p) => [button(p, 'Alterar senha')],
  };
  const problems: string[] = [];
  await forEachScreen(page, async (path) => {
    await page.addStyleTag({ content: 'html { font-size: 200% !important; }' });
    await page.evaluate(() => new Promise((r) => requestAnimationFrame(() => r(null))));
    problems.push(...(await layoutProblems(page)).map((p) => `${path}: ${p}`));
    for (const loc of primary[path]!(page)) {
      await loc.scrollIntoViewIfNeeded();
      const ok = await loc.evaluate((el) => {
        const r = el.getClientRects()[0]!; // link em linha quebrado em duas linhas: 1ª caixa
        const top = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
        return (
          r.width > 0 &&
          r.left >= 0 &&
          r.right <= innerWidth &&
          !!top &&
          (top === el || el.contains(top))
        );
      });
      if (!ok) problems.push(`${path}: ação não acionável: ${loc}`);
    }
  });
  expect(problems).toEqual([]);
});

// ---------------------------------------------------------------------------------------------
// Teclado: ordem de tabulação, armadilha e indicador de foco
// ---------------------------------------------------------------------------------------------

interface FocusStop {
  id: number;
  name: string;
  top: number;
  bottom: number;
  left: number;
  focusVisible: boolean;
  outlineVisible: boolean;
  ringContrast: number | null;
  multi: boolean;
}

/** Descreve o elemento focado agora: posição, :focus-visible, anel de foco e contraste dele. */
function currentFocus(page: Page): Promise<FocusStop | null> {
  return page.evaluate(() => {
    const w = window as unknown as { __ids?: WeakMap<Element, number>; __n?: number };
    w.__ids ??= new WeakMap();
    const el = document.activeElement as HTMLElement | null;
    if (!el || el === document.body || el === document.documentElement) return null;
    if (!w.__ids.has(el)) w.__ids.set(el, (w.__n = (w.__n ?? 0) + 1));
    const parse = (c: string): number[] => {
      const n = (c.match(/-?[\d.]+/g) ?? []).map(Number);
      if (c.startsWith('color(')) return [n[0]! * 255, n[1]! * 255, n[2]! * 255, n[3] ?? 1];
      return [n[0]!, n[1]!, n[2]!, n[3] ?? 1];
    };
    const lum = ([r, g, b]: number[]) => {
      const f = (v: number) => ((v /= 255) <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4);
      return 0.2126 * f(r!) + 0.7152 * f(g!) + 0.0722 * f(b!);
    };
    const cs = getComputedStyle(el);
    const outline = parse(cs.outlineColor);
    const outlineVisible =
      cs.outlineStyle !== 'none' && parseFloat(cs.outlineWidth) > 0 && (outline[3] ?? 1) > 0;
    let bg = [255, 255, 255, 1];
    for (let n: Element | null = el.parentElement; n; n = n.parentElement) {
      const c = parse(getComputedStyle(n).backgroundColor);
      if ((c[3] ?? 1) === 1) {
        bg = c;
        break;
      }
    }
    const [hi, lo] = [lum(outline), lum(bg)].sort((a, b) => b - a);
    const r = el.getBoundingClientRect();
    return {
      id: w.__ids.get(el)!,
      name: `${el.tagName.toLowerCase()} "${el.getAttribute('aria-label') ?? (el.textContent || el.id).trim().slice(0, 30)}"`,
      top: r.top + scrollY,
      bottom: r.bottom + scrollY,
      left: r.left + scrollX,
      focusVisible: el.matches(':focus-visible'),
      outlineVisible: outlineVisible || cs.boxShadow !== 'none',
      multi: el instanceof HTMLInputElement && el.type === 'date',
      ringContrast: outlineVisible ? (hi! + 0.05) / (lo! + 0.05) : null,
    };
  });
}

/** O que o Tab deveria alcançar na página atual (mesmo mapa de ids de `currentFocus`). */
function expectedTabbables(page: Page): Promise<{ id: number; name: string }[]> {
  return page.evaluate(() => {
    const w = window as unknown as { __ids?: WeakMap<Element, number>; __n?: number };
    w.__ids ??= new WeakMap();
    const id = (el: Element) => {
      if (!w.__ids!.has(el)) w.__ids!.set(el, (w.__n = (w.__n ?? 0) + 1));
      return w.__ids!.get(el)!;
    };
    const seenGroups = new Set<string>();
    return Array.from(
      document.querySelectorAll<HTMLElement>(
        'a[href], button, input, select, textarea, [tabindex]',
      ),
    )
      .filter((el) => {
        if (el.tabIndex < 0 || (el as HTMLButtonElement).disabled) return false;
        if (el instanceof HTMLInputElement && el.type === 'hidden') return false;
        if (!el.checkVisibility({ visibilityProperty: true })) return false;
        if (el instanceof HTMLInputElement && el.type === 'radio') {
          // o Tab visita um só radio por grupo: o marcado, ou o primeiro se nenhum
          const checked = document.querySelector<HTMLInputElement>(
            `input[type=radio][name="${el.name}"]:checked`,
          );
          if (checked ? checked !== el : seenGroups.has(el.name)) return false;
          seenGroups.add(el.name);
        }
        return true;
      })
      .map((el) => ({
        id: id(el),
        name: `${el.tagName.toLowerCase()} "${(el.getAttribute('aria-label') ?? (el.textContent || el.id)).trim().slice(0, 30)}"`,
      }));
  });
}

/** Tab (ou Shift+Tab) do início do documento até sair/reciclar. Falha em armadilha. */
async function tabThrough(page: Page, key: 'Tab' | 'Shift+Tab'): Promise<FocusStop[]> {
  const stops: FocusStop[] = [];
  for (let i = 0; i < 120; i++) {
    await page.keyboard.press(key);
    const stop = await currentFocus(page);
    if (!stop || stop.id === stops[0]?.id) return stops; // saiu do documento ou reciclou
    if (stop.id === stops.at(-1)?.id && stop.multi) continue; // <input type=date>: um Tab por segmento
    if (stop.id === stops.at(-1)?.id) throw new Error(`preso em ${stop.name} após ${key}`);
    stops.push(stop);
  }
  throw new Error(`${key} não terminou em 120 passos (armadilha ou laço)`);
}

async function reloadScreen(page: Page, path: string): Promise<void> {
  await page.reload();
  await expect(page.getByRole('heading', { level: 1 }).first()).toBeVisible();
  if (path === '/tasks') {
    await expect(page.getByRole('list', { name: 'Lista de tarefas' })).toBeVisible();
  }
}

test('ordem de tabulação = ordem visual, sem tabindex positivo, sem armadilha, Shift+Tab volta (FE-21 CA-05, CA-07)', async ({
  page,
}) => {
  const problems: string[] = [];
  await forEachScreen(page, async (path) => {
    await reloadScreen(page, path);
    const positive = await page.evaluate(
      () =>
        Array.from(document.querySelectorAll('[tabindex]')).filter(
          (e) => Number(e.getAttribute('tabindex')) > 0,
        ).length,
    );
    if (positive) problems.push(`${path}: ${positive} tabindex positivo`);

    const expected = await expectedTabbables(page);
    const forward = await tabThrough(page, 'Tab');
    const reached = new Set(forward.map((s) => s.id));
    for (const e of expected.filter((e) => !reached.has(e.id))) {
      problems.push(`${path}: Tab não alcança ${e.name}`);
    }

    const TOL = 8; // px
    forward.slice(1).forEach((b, i) => {
      const a = forward[i]!;
      const nextRow = b.top >= a.bottom - TOL;
      const sameRow = b.top < a.bottom - TOL && b.bottom > a.top + TOL && b.left >= a.left - TOL;
      if (!nextRow && !sameRow)
        problems.push(`${path}: ${a.name} -> ${b.name} sai da ordem visual`);
    });

    await reloadScreen(page, path);
    // Shift+Tab: do último focável de volta ao primeiro, na ordem inversa do Tab
    const last = forward.at(-1)!;
    for (let i = 0; i < 150 && (await currentFocus(page))?.id !== last.id; i++) {
      await page.keyboard.press('Tab');
    }
    const backward: number[] = [last.id];
    for (let i = 0; i < 150 && backward.at(-1) !== forward[0]!.id; i++) {
      await page.keyboard.press('Shift+Tab');
      const stop = await currentFocus(page);
      if (stop && stop.id !== backward.at(-1)) backward.push(stop.id);
    }
    if (
      backward.join() !==
      [...forward]
        .reverse()
        .map((s) => s.id)
        .join()
    ) {
      problems.push(`${path}: Shift+Tab não percorre o inverso do Tab`);
    }
  });
  expect(problems).toEqual([]);
});

for (const scheme of ['light', 'dark'] as const) {
  test(`todo focável por Tab tem indicador de foco visível, com contraste >= 3:1 (tema ${scheme}) (FE-04 CA-11, FE-21 CA-06)`, async ({
    page,
  }) => {
    await page.emulateMedia({ colorScheme: scheme });
    const problems: string[] = [];
    let stops = 0;
    await forEachScreen(page, async (path) => {
      await reloadScreen(page, path);
      for (const s of await tabThrough(page, 'Tab')) {
        stops++;
        if (!s.focusVisible) problems.push(`${path}: ${s.name} sem :focus-visible por teclado`);
        if (!s.outlineVisible) problems.push(`${path}: ${s.name} sem indicador de foco`);
        if (s.ringContrast !== null && s.ringContrast < 3) {
          problems.push(`${path}: ${s.name} anel com contraste ${s.ringContrast.toFixed(2)}`);
        }
      }
    });
    expect(stops).toBeGreaterThan(50);
    expect(problems).toEqual([]);
  });
}

// ---------------------------------------------------------------------------------------------
// Fluxos completos só com teclado (nenhum click): FE-21 CA-04, FE-08 CA-19, FE-11 CA-16,
// FE-12 CA-20, FE-17 CA-24, FE-18 CA-26
// ---------------------------------------------------------------------------------------------

async function typeInto(page: Page, target: Locator, text: string): Promise<void> {
  await tabTo(page, target);
  await page.keyboard.type(text);
}

async function press(page: Page, target: Locator, key = 'Enter'): Promise<void> {
  await tabTo(page, target);
  await page.keyboard.press(key);
}

test.describe('só teclado', () => {
  test('cadastro -> login -> sair (FE-08 CA-19, FE-21 CA-04)', async ({ page }) => {
    const user = newUser();
    await page.goto('/register');
    await typeInto(page, page.getByLabel('E-mail', { exact: true }), user.email);
    await typeInto(page, page.getByLabel('Senha', { exact: true }), user.password);
    await typeInto(page, page.getByLabel('Confirmar senha', { exact: true }), user.password);
    // Enter com o botão ainda desabilitado não envia: espera a validação do formulário assentar
    await expect(page.getByRole('button', { name: 'Criar conta' })).toBeEnabled();
    await page.keyboard.press('Enter');
    await expect(page.getByText('Conta criada com sucesso.')).toBeVisible();

    await typeInto(page, page.getByLabel('Senha', { exact: true }), user.password);
    await page.keyboard.press('Enter');
    await expect(page.getByRole('heading', { level: 1, name: 'Tarefas' })).toBeVisible();

    await press(page, page.getByRole('button', { name: 'Sair' }));
    await expect(page).toHaveURL(/\/login/);
    await expect(page.getByText('Você saiu da sua conta.')).toBeVisible();
  });

  test('criar (com data), concluir, filtrar/buscar, editar e remover com diálogo (FE-17 CA-24, FE-18 CA-26, FE-21 CA-04)', async ({
    page,
  }) => {
    await registerAndLoginViaApi(page);

    async function create(title: string, withDate: boolean): Promise<void> {
      await press(page, page.getByRole('link', { name: 'Nova tarefa' }).first());
      await expect(page.getByRole('heading', { level: 1, name: 'Nova tarefa' })).toBeVisible();
      await typeInto(page, page.getByLabel('Título', { exact: true }), title);
      await typeInto(page, page.getByLabel('Descrição', { exact: true }), 'Descrição digitada');
      const priority = page.getByLabel('Prioridade', { exact: true });
      await tabTo(page, priority);
      await page.keyboard.type('A'); // "Alta"
      await expect(priority).toHaveValue('High');
      if (withDate) {
        const due = page.getByLabel('Vencimento', { exact: true });
        await tabTo(page, due);
        await page.keyboard.type('11112099'); // dia = mês: vale em dd/mm e em mm/dd
        await expect(due).toHaveValue('2099-11-11');
      }
      await press(page, page.getByRole('button', { name: 'Criar tarefa' }));
      await expect(page.getByRole('heading', { level: 2, name: title })).toBeVisible();
    }

    await create('Alfa teclado', true);
    await create('Beta teclado', false);

    await press(page, page.getByRole('checkbox', { name: 'Concluir: Beta teclado' }), 'Space');
    await expect(page.getByRole('checkbox', { name: 'Reabrir: Beta teclado' })).toBeChecked();

    await press(page, page.getByRole('radio', { name: 'Todas' }), 'ArrowDown');
    await expect(page.getByRole('radio', { name: 'Pendentes' })).toBeChecked();
    await expect(page).toHaveURL(/status=pending/);
    await expect(page.getByRole('heading', { level: 2, name: 'Beta teclado' })).toHaveCount(0);
    await typeInto(page, page.getByRole('searchbox', { name: 'Buscar' }), 'Alfa');
    await expect(page).toHaveURL(/search=Alfa/);
    await press(page, page.getByRole('button', { name: 'Limpar filtros' }));
    await expect(page.getByRole('heading', { level: 2, name: 'Beta teclado' })).toBeVisible();

    await press(page, page.getByRole('link', { name: 'Editar: Alfa teclado' }));
    await expect(page.getByRole('heading', { level: 1, name: 'Editar tarefa' })).toBeVisible();
    await expect(page.getByLabel('Vencimento', { exact: true })).toHaveValue('2099-11-11');
    await tabTo(page, page.getByLabel('Título', { exact: true }));
    await page.keyboard.press('Control+A');
    await page.keyboard.type('Alfa editada');
    await press(page, page.getByRole('button', { name: 'Salvar alterações' }));
    await expect(page.getByRole('heading', { level: 2, name: 'Alfa editada' })).toBeVisible();

    await press(page, page.getByRole('button', { name: 'Remover: Alfa editada' }));
    const dialog = page.getByRole('alertdialog', { name: 'Remover tarefa' });
    await expect(dialog).toBeVisible();
    await press(page, dialog.getByRole('button', { name: 'Remover' }));
    await expect(page.getByRole('heading', { level: 2, name: 'Alfa editada' })).toHaveCount(0);
  });

  test('erro de validação leva o foco ao primeiro campo inválido, só por teclado (FE-18 CA-26)', async ({
    page,
  }) => {
    await registerAndLoginViaApi(page);
    await createTask(page, 'Tarefa para invalidar');
    await press(page, page.getByRole('link', { name: 'Editar: Tarefa para invalidar' }));
    await expect(page.getByRole('heading', { level: 1, name: 'Editar tarefa' })).toBeVisible();
    // o título continua válido: o primeiro campo com erro é a descrição (2001 > 2000)
    const description = page.getByLabel('Descrição', { exact: true });
    await description.fill('x'.repeat(2001));
    await press(page, page.getByRole('button', { name: 'Salvar alterações' }));
    await expect(description).toBeFocused();
    await expect(description).toHaveAttribute('aria-invalid', 'true');
  });

  test('editar perfil -> trocar senha -> login com a nova -> sair (FE-11 CA-16, FE-12 CA-20, FE-21 CA-04)', async ({
    page,
  }) => {
    const user = await registerAndLoginViaApi(page);
    await press(page, page.getByRole('banner').getByRole('link'));
    await expect(page.getByRole('heading', { level: 1, name: 'Minha conta' })).toBeVisible();

    // o formulário é preenchido quando o perfil chega: digitar antes seria sobrescrito
    await expect(page.getByLabel('Nome de exibição', { exact: true })).not.toHaveValue('');
    await tabTo(page, page.getByLabel('Nome de exibição', { exact: true }));
    await page.keyboard.press('Control+A');
    await page.keyboard.type('Nome Teclado');
    await expect(page.getByRole('button', { name: 'Salvar', exact: true })).toBeEnabled();
    await page.keyboard.press('Enter');
    await expect(page.getByRole('link', { name: 'Nome Teclado' })).toBeVisible();

    await press(page, page.getByRole('link', { name: 'Alterar senha' }));
    await expect(page.getByRole('heading', { level: 1, name: 'Alterar senha' })).toBeVisible();
    await typeInto(page, page.getByLabel('Senha atual', { exact: true }), user.password);
    await typeInto(page, page.getByLabel('Nova senha', { exact: true }), NEW_TEST_PASSWORD);
    await typeInto(
      page,
      page.getByLabel('Confirmar nova senha', { exact: true }),
      NEW_TEST_PASSWORD,
    );
    await page.keyboard.press('Enter');
    await expect(page).toHaveURL(/\/login/);
    await expect(page.getByText('Sua senha foi alterada.')).toBeVisible();

    await typeInto(page, page.getByLabel('Senha', { exact: true }), NEW_TEST_PASSWORD);
    await page.keyboard.press('Enter');
    await expect(page.getByRole('heading', { level: 1, name: 'Tarefas' })).toBeVisible();
    await press(page, page.getByRole('button', { name: 'Sair' }));
    await expect(page).toHaveURL(/\/login/);
  });

  test('excluir conta (diálogo) só por teclado (FE-21 CA-04)', async ({ page }) => {
    await registerAndLoginViaApi(page);
    await page.goto('/account');
    await press(page, page.getByRole('button', { name: 'Excluir minha conta' }));
    const dialog = page.getByRole('alertdialog', { name: 'Excluir sua conta' });
    await expect(dialog).toBeVisible();
    await typeInto(page, dialog.getByLabel('Confirme sua senha'), TEST_PASSWORD);
    await press(page, dialog.getByRole('button', { name: 'Excluir permanentemente' }));
    await expect(page).toHaveURL(/\/login/);
    await expect(page.getByText('Sua conta foi excluída.')).toBeVisible();
  });
});

// ---------------------------------------------------------------------------------------------
// Leitor de tela: o que é estruturalmente verificável (FE-21 CA-11 a CA-16)
// ---------------------------------------------------------------------------------------------

test('todas as telas: um <h1> único e sem salto de nível (FE-21 CA-16)', async ({ page }) => {
  const problems: string[] = [];
  await forEachScreen(page, async (path) => {
    const h1 = await page.getByRole('heading', { level: 1 }).count();
    if (h1 !== 1) problems.push(`${path}: ${h1} elementos <h1>`);
    const { violations } = await new AxeBuilder({ page })
      .withRules(['page-has-heading-one', 'heading-order'])
      .analyze();
    for (const v of violations) problems.push(`${path}: ${v.id}`);
  });
  expect(problems).toEqual([]);
});

test('todo campo tem nome acessível (FE-21 CA-12)', async ({ page }) => {
  let fields = 0;
  await forEachScreen(page, async () => {
    for (const field of await page.locator('input:not([type=hidden]), select, textarea').all()) {
      if (!(await field.isVisible())) continue;
      fields++;
      await expect(field).toHaveAccessibleName(/\S/);
    }
  });
  expect(fields).toBeGreaterThan(15);
});

async function expectInvalidExposesMessage(field: Locator): Promise<void> {
  await expect(field).toHaveAttribute('aria-invalid', 'true');
  const id = (await field.getAttribute('aria-describedby'))?.split(' ')[0];
  expect(id, 'campo inválido sem aria-describedby').toBeTruthy();
  const text = (await field.page().locator(`#${id}`).innerText()).replace(/\s+/g, ' ').trim();
  expect(text.length).toBeGreaterThan(0);
  await expect(field).toHaveAccessibleDescription(text);
}

test('campo inválido expõe aria-invalid e a mensagem pela descrição acessível (FE-21 CA-12)', async ({
  page,
}) => {
  await page.goto('/register');
  await page.getByLabel('E-mail', { exact: true }).fill('invalido');
  await page.getByLabel('Senha', { exact: true }).fill('curta');
  await page.getByLabel('Nome de exibição (opcional)').focus();
  await expectInvalidExposesMessage(page.getByLabel('E-mail', { exact: true }));
  await expectInvalidExposesMessage(page.getByLabel('Senha', { exact: true }));

  await page.goto('/login');
  await page.getByRole('button', { name: 'Entrar' }).click();
  await expectInvalidExposesMessage(page.getByLabel('E-mail', { exact: true }));
  await expectInvalidExposesMessage(page.getByLabel('Senha', { exact: true }));

  await registerAndLogin(page);
  await page.goto('/tasks/new');
  await page.getByRole('button', { name: 'Criar tarefa' }).click();
  await expectInvalidExposesMessage(page.getByLabel('Título', { exact: true }));

  await page.goto('/account/password');
  await page.getByRole('button', { name: 'Alterar senha' }).click();
  await expectInvalidExposesMessage(page.getByLabel('Senha atual', { exact: true }));
  await expectInvalidExposesMessage(page.getByLabel('Nova senha', { exact: true }));
});

test('erro é anunciado em região assertiva; sucesso e aviso em região educada (FE-21 CA-11)', async ({
  page,
}) => {
  const user = newUser();
  await page.goto('/register');
  await page.getByLabel('E-mail', { exact: true }).fill(user.email);
  await page.getByLabel('Senha', { exact: true }).fill(user.password);
  await page.getByLabel('Confirmar senha', { exact: true }).fill(user.password);
  await page.getByRole('button', { name: 'Criar conta' }).click();
  const success = page.getByRole('status').filter({ hasText: 'Conta criada com sucesso.' });
  await expect(success).toHaveAttribute('aria-live', 'polite');

  await page.getByLabel('Senha', { exact: true }).fill('Senha-errada-999');
  await page.getByRole('button', { name: 'Entrar' }).click();
  await expect(page.getByRole('alert')).toHaveAttribute('aria-live', 'assertive');
  await expect(page.getByRole('alert')).toHaveText(/E-mail ou senha inválidos\./);

  await page.getByLabel('Senha', { exact: true }).fill(user.password);
  await page.getByRole('button', { name: 'Entrar' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Tarefas' })).toBeVisible();
  await createTask(page, 'Tarefa do aviso');
  await page.getByRole('checkbox', { name: 'Concluir: Tarefa do aviso' }).check();
  await expect(
    page.locator('[aria-live="polite"]').filter({ hasText: 'Tarefa concluída.' }),
  ).toHaveCount(1);

  await page.getByRole('link', { name: 'Nova tarefa' }).click();
  await page.getByLabel('Vencimento', { exact: true }).fill('2020-01-15');
  await expect(page.getByRole('status').filter({ hasText: 'Esta data já passou' })).toBeVisible();

  await page.goto('/tasks'); // sai do formulário sujo sem acionar o guard de descarte
  await page.getByRole('button', { name: 'Sair' }).click();
  await expect(
    page.getByRole('status').filter({ hasText: 'Você saiu da sua conta.' }),
  ).toHaveAttribute('aria-live', 'polite');
});

test('lista: ações nomeadas pela tarefa, contagem em role=status que muda ao filtrar, texto além de cor (FE-21 CA-13, CA-14, CA-15)', async ({
  page,
}) => {
  await registerAndLogin(page);
  await createTask(page, 'Tarefa pendente', 'Média');
  await createTask(page, 'Tarefa concluída', 'Baixa');
  await page.getByRole('checkbox', { name: 'Concluir: Tarefa concluída' }).check();
  await expect(page.getByRole('checkbox', { name: 'Reabrir: Tarefa concluída' })).toBeChecked();
  await createTask(page, 'Tarefa atrasada', 'Alta', '2020-01-15');

  for (const title of ['Tarefa pendente', 'Tarefa concluída', 'Tarefa atrasada']) {
    const item = taskItem(page, title);
    await expect(item.getByRole('checkbox')).toHaveAccessibleName(new RegExp(title));
    await expect(item.getByRole('link', { name: /Editar/ })).toHaveAccessibleName(
      new RegExp(title),
    );
    await expect(item.getByRole('button', { name: /Remover/ })).toHaveAccessibleName(
      new RegExp(title),
    );
  }

  const count = page
    .getByRole('status')
    .filter({ hasText: /encontradas?/ })
    .first();
  await expect(count).toHaveText('3 tarefas encontradas');
  await page.getByRole('radio', { name: 'Pendentes' }).check();
  await expect(count).toHaveText('2 tarefas encontradas com os filtros aplicados');
  await page.getByRole('radio', { name: 'Concluídas' }).check();
  await expect(count).toHaveText('1 tarefa encontrada com os filtros aplicados');
  await page.getByRole('radio', { name: 'Todas' }).check();

  const pending = taskItem(page, 'Tarefa pendente');
  const done = taskItem(page, 'Tarefa concluída');
  const late = taskItem(page, 'Tarefa atrasada');
  await expect(pending.locator('dd', { hasText: /^Média$/ })).toBeVisible();
  await expect(pending.locator('dd', { hasText: /^Pendente$/ })).toBeVisible();
  await expect(done.locator('dd', { hasText: /^Baixa$/ })).toBeVisible();
  await expect(done.locator('dd', { hasText: /^Concluída$/ })).toBeVisible();
  await expect(late.locator('dd', { hasText: /^Alta$/ })).toBeVisible();
  await expect(late.getByText('Atrasada', { exact: true })).toBeVisible();
  await expect(pending.getByText('Atrasada', { exact: true })).toHaveCount(0);
  await expect(done.getByText('Atrasada', { exact: true })).toHaveCount(0);
});
