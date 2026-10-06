import { formatDueDate } from './task-date.util';

describe('formatDueDate', () => {
  // FE-02, CA-08: a data é formatada por string; `new Date('2026-01-01')` em UTC-3 seria
  // 31/12 — o resultado não pode depender do fuso.
  it('exibe 1 de janeiro para "2026-01-01" mesmo onde Date daria 31/12 (UTC-3) (CA-08)', () => {
    const asDate = new Date('2026-01-01');
    expect(asDate.toLocaleDateString('pt-BR', { timeZone: 'America/Sao_Paulo' })).toBe(
      '31/12/2025',
    );
    expect(formatDueDate('2026-01-01')).toBe('01/01/2026');
  });

  it('devolve null quando não há vencimento', () => {
    expect(formatDueDate(null)).toBeNull();
  });
});
