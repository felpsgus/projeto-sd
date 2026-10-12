import { buildSafeReturnUrl, resolveReturnUrl } from './return-url.util';

describe('buildSafeReturnUrl (CA-09 — proteção contra open redirect)', () => {
  it('aceita um caminho interno relativo', () => {
    expect(buildSafeReturnUrl('/tasks/abc/edit')).toBe('/tasks/abc/edit');
  });

  it.each([
    'https://exemplo.com',
    '//exemplo.com',
    'javascript:alert(1)',
    'http://exemplo.com/tasks',
  ])('rejeita %s e devolve a rota padrão', (malicious) => {
    expect(buildSafeReturnUrl(malicious)).toBe('/tasks');
  });
});

describe('resolveReturnUrl', () => {
  it('devolve /tasks quando não há returnUrl', () => {
    expect(resolveReturnUrl(null)).toBe('/tasks');
    expect(resolveReturnUrl(undefined)).toBe('/tasks');
  });

  it('devolve o caminho interno informado', () => {
    expect(resolveReturnUrl('/tasks/new')).toBe('/tasks/new');
  });
});
