// Resumo de cobertura do frontend para o PR + aviso de queda contra o baseline (FE-23 CA-10).
// Uso: node scripts/coverage-summary.mjs  (a partir de frontend/). Nunca falha o build: o piso é do vitest.
import { appendFileSync, readFileSync } from 'node:fs';

const total = JSON.parse(readFileSync('coverage/frontend/coverage-summary.json', 'utf8')).total;
const baseline = JSON.parse(readFileSync('../coverage-baseline.json', 'utf8')).frontendLines;

const lines = ['### Cobertura do frontend', '', '| Métrica | % |', '|---|---|'];
for (const k of ['lines', 'statements', 'functions', 'branches'])
  lines.push(`| ${k} | ${total[k].pct} |`);

// FE-23 CA-11: as exclusões aparecem junto do número, não ficam só no angular.json.
const excluded = JSON.parse(readFileSync('angular.json', 'utf8')).projects.frontend.architect.test
  .options.coverageExclude;
lines.push('', `Fora da cobertura: ${excluded.map((glob) => `\`${glob}\``).join(', ')}`);

// ponytail: baseline manual em coverage-baseline.json; trocar por artefato da `main` se virar incômodo.
if (total.lines.pct < baseline) {
  const msg = `Cobertura de linhas do frontend caiu: ${total.lines.pct}% < baseline ${baseline}%`;
  console.log(`::warning::${msg}`);
  lines.push('', `:warning: ${msg}`);
}

const out = lines.join('\n') + '\n';
if (process.env.GITHUB_STEP_SUMMARY) appendFileSync(process.env.GITHUB_STEP_SUMMARY, out);
else console.log(out);
