import { defineConfig } from 'vitest/config';

// Pisos de cobertura (FE-23, convenções seção 5). Lido pelo builder `@angular/build:unit-test`
// via `runnerConfig` (angular.json). Inclusão/exclusão ficam no angular.json (coverageInclude/
// coverageExclude) para aparecerem junto com o resto da configuração de teste.
export default defineConfig({
  test: {
    coverage: {
      thresholds: {
        // Global: >= 75% de linhas.
        lines: 75,
        // Serviços e lógica de estado: >= 80% (agregado dos arquivos de cada glob).
        'src/app/core/**/*.ts': { lines: 80 },
        'src/app/**/*.store.ts': { lines: 80 },
        'src/app/**/*.guard.ts': { lines: 80 },
        'src/app/**/*.interceptor.ts': { lines: 80 },
      },
    },
  },
});
