// Tipos mínimos de `node:fs` para o spec de catálogo (sem adicionar @types/node).
declare module 'node:fs' {
  export interface Dirent {
    name: string;
    isDirectory(): boolean;
  }
  export function readdirSync(path: string, options: { withFileTypes: true }): Dirent[];
  export function readFileSync(path: string, encoding: 'utf-8'): string;
}
