import { readdirSync, readFileSync } from 'node:fs';

import * as catalog from './error-messages';

// FE-03 CA-11: nenhuma tela escreve mensagem de erro fora do catálogo. A "busca no código"
// do critério, automatizada: lê todo .ts/.html de src/app (fora de specs e do catálogo).
// `import.meta.glob` não é aceito pelo build de teste do Angular; usa-se node:fs (cwd = frontend/).
function walk(dir: string): string[] {
  return readdirSync(dir, { withFileTypes: true }).flatMap((e) => {
    const path = `${dir}/${e.name}`;
    return e.isDirectory() ? walk(path) : [path];
  });
}

const sources: [string, string][] = walk('src/app')
  .filter(
    (f) => /\.(html|ts)$/.test(f) && !f.endsWith('.spec.ts') && !f.endsWith('/error-messages.ts'),
  )
  .map((f) => [f, readFileSync(f, 'utf-8')]);

const catalogMessages = [
  ...Object.values(catalog.ERROR_MESSAGES),
  ...Object.values(catalog).filter((v) => typeof v === 'string'),
] as string[];

describe('catálogo de mensagens de erro (FE-03 CA-11)', () => {
  it('encontra arquivos para varrer', () => {
    expect(sources.length).toBeGreaterThan(50);
  });

  it('nenhuma mensagem do catálogo é repetida literalmente fora dele', () => {
    const offenders = sources.flatMap(([file, text]) =>
      catalogMessages.filter((m) => text.includes(m)).map((m) => `${file}: "${m}"`),
    );
    expect(offenders).toEqual([]);
  });

  it('nenhum template escreve texto literal em role="alert" ou em message="" de <app-error-state>', () => {
    // Literal = qualquer caractere que não seja espaço, `<` ou `{` logo após o `>` do elemento
    // de alerta (só interpolação/tag filha é aceita); ou `message="..."` sem colchetes / `[message]` com literal contendo letras (o `?? ''` vazio da lista é legítimo).
    const alertLiteral = /role="alert"[^>]*>\s*[^\s<{]/;
    const stateLiteral = /<app-error-state[^>]*(\smessage="|\[message\]="[^"']*'[^'"]*[A-Za-zÀ-ú])/;
    const offenders = sources
      .filter(([, text]) => alertLiteral.test(text) || stateLiteral.test(text))
      .map(([file]) => file);
    expect(offenders).toEqual([]);
  });
});
