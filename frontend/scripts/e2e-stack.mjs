// Comando único: prepara pré-requisitos, sobe a stack real em container e roda o Playwright.
// Uso: npm run e2e:stack [-- args do playwright]. Funciona igual no Windows e no runner Linux
// (precisa de docker compose e pwsh 7+ só para gerar chaves/SQL na primeira vez).
import { spawnSync } from 'node:child_process';
import { existsSync } from 'node:fs';
import { resolve } from 'node:path';

const root = resolve(import.meta.dirname, '..', '..');
const baseUrl = process.env.E2E_BASE_URL ?? 'http://localhost';

function run(cmd, args, cwd = root) {
  const r = spawnSync(cmd, args, { cwd, stdio: 'inherit', shell: process.platform === 'win32' });
  if (r.status !== 0) process.exit(r.status ?? 1);
}

if (!existsSync(resolve(root, '.secrets/jwt/private.pem'))) {
  run('pwsh', ['-NoProfile', '-File', 'scripts/new-jwt-keys.ps1']);
}
if (!existsSync(resolve(root, 'artifacts/sql/02-tasks.sql'))) {
  run('pwsh', ['-NoProfile', '-File', 'scripts/new-migrations-sql.ps1']);
}
run('docker', ['compose', '--profile', 'full', 'up', '-d', '--build']);

// Pronto = o nginx já alcança o gateway e o gateway já alcança o Identity (401 de credencial, não 502/503).
const deadline = Date.now() + 120_000;
for (;;) {
  try {
    const res = await fetch(`${baseUrl}/api/auth/login`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email: 'ready@example.test', password: 'x' }),
    });
    if (res.status === 401 || res.status === 400) break;
  } catch {
    // ainda subindo
  }
  if (Date.now() > deadline) {
    console.error('Stack não ficou pronta em 120 s.');
    process.exit(1);
  }
  await new Promise((r) => setTimeout(r, 2000));
}

run(
  'npx',
  ['playwright', 'test', '--project=chromium', '--project=mobile-360', ...process.argv.slice(2)],
  resolve(root, 'frontend'),
);
