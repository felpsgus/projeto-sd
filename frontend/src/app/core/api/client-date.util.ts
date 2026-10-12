/**
 * Formata uma data como `yyyy-MM-dd` usando o fuso **local** do navegador (FD-17/FD-19).
 *
 * Armadilha que este utilitário existe para evitar: `date.toISOString().slice(0, 10)`
 * devolve a data em **UTC**, não a local — em `UTC-3`, depois das 21h, isso manda o dia
 * seguinte ao backend, reintroduzindo o bug que a decisão D-18 resolveu. `Intl.DateTimeFormat`
 * com o locale `en-CA` já formata como `yyyy-MM-dd` a partir dos componentes locais da data,
 * sem qualquer conversão para UTC.
 */
export function toLocalDateString(date: Date, timeZone?: string): string {
  // `timeZone` só é passado em teste (para simular fusos diferentes do fuso da máquina
  // que roda o teste); em produção o parâmetro fica de fora e o Intl usa o fuso local do
  // navegador — que é exatamente o que FD-17/FD-19 pedem.
  return new Intl.DateTimeFormat('en-CA', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    ...(timeZone ? { timeZone } : {}),
  }).format(date);
}

/** Nome do header enviado em toda requisição à API (FD-17). */
export const CLIENT_DATE_HEADER = 'X-Client-Date';
