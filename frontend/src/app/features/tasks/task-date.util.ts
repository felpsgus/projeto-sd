/**
 * Formata uma data pura `yyyy-MM-dd` (vinda de `TaskResponse.dueDate`) como `dd/MM/yyyy`
 * por manipulação de string — nunca via `new Date()` (FE-02/FE-15, notas técnicas), que
 * deslocaria o dia em fusos negativos (ex.: `UTC-3`).
 */
export function formatDueDate(dueDate: string | null): string | null {
  if (!dueDate) {
    return null;
  }
  const [year, month, day] = dueDate.split('-');
  if (!year || !month || !day) {
    return dueDate;
  }
  return `${day}/${month}/${year}`;
}
