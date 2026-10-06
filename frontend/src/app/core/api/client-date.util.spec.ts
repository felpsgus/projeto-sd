import { toLocalDateString } from './client-date.util';

describe('toLocalDateString', () => {
  it('formata como yyyy-MM-dd', () => {
    const date = new Date('2026-08-20T12:00:00Z');
    expect(toLocalDateString(date, 'UTC')).toBe('2026-08-20');
  });

  // FE-02, CA-15: 21h locais em UTC-3 já é meia-noite em UTC do dia seguinte — o header
  // deve refletir a data LOCAL (20/08), nunca a UTC (21/08). Este é o teste que impede a
  // reintrodução do bug de D-18.
  it('usa a data local em UTC-3, não a data UTC (CA-15)', () => {
    const date = new Date('2026-08-20T21:30:00-03:00'); // == 2026-08-21T00:30:00Z
    expect(toLocalDateString(date, 'America/Sao_Paulo')).toBe('2026-08-20');
    expect(date.toISOString().slice(0, 10)).toBe('2026-08-21'); // prova de que UTC daria o dia errado
  });

  // FE-02, CA-16: o mesmo vale do outro lado do globo, num fuso adiantado em relação a UTC.
  it('usa a data local em UTC+9 (CA-16)', () => {
    const date = new Date('2026-08-20T23:30:00Z'); // 2026-08-21 08:30 em Tóquio
    expect(toLocalDateString(date, 'Asia/Tokyo')).toBe('2026-08-21');
  });
});
