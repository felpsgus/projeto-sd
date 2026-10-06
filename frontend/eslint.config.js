// @ts-check
const eslint = require('@eslint/js');
const { defineConfig } = require('eslint/config');
const tseslint = require('typescript-eslint');
const angular = require('angular-eslint');
const prettierConfig = require('eslint-config-prettier');

module.exports = defineConfig([
  {
    ignores: ['coverage/**', 'dist/**', 'playwright-report/**', 'test-results/**'],
  },
  {
    files: ['**/*.ts'],
    extends: [
      eslint.configs.recommended,
      tseslint.configs.recommended,
      tseslint.configs.stylistic,
      angular.configs.tsRecommended,
      prettierConfig,
    ],
    processor: angular.processInlineTemplates,
    rules: {
      '@angular-eslint/directive-selector': [
        'error',
        {
          type: 'attribute',
          prefix: 'app',
          style: 'camelCase',
        },
      ],
      '@angular-eslint/component-selector': [
        'error',
        {
          type: 'element',
          prefix: 'app',
          style: 'kebab-case',
        },
      ],
      // Convenção 2.2: `any` é proibido salvo justificativa escrita ao lado (comentário no código).
      '@typescript-eslint/no-explicit-any': 'error',
      // FE-23 CA-21: nada de log de payload/estado no build; `error` só em main.ts (bootstrap).
      'no-console': ['error', { allow: ['warn', 'error'] }],
      // FE-05, CA-11/CA-13 (FD-01, RN-AUTH-20): nenhuma credencial em storage, cookie legível por JS
      // nem variável chamada refreshToken — o refresh token é um cookie HttpOnly que o JS nunca vê.
      'no-restricted-globals': [
        'error',
        { name: 'localStorage', message: 'Sessão não usa storage (FD-01).' },
        { name: 'sessionStorage', message: 'Sessão não usa storage (FD-01).' },
      ],
      'no-restricted-syntax': [
        'error',
        {
          selector: "MemberExpression[object.name='document'][property.name='cookie']",
          message: 'Sessão não lê nem escreve document.cookie (FD-01).',
        },
        {
          selector: "Identifier[name='refreshToken']",
          message: 'O frontend nunca vê o refresh token (FD-01).',
        },
      ],
    },
  },
  {
    // Os testes de segurança de FE-05 e o E2E antivazamento (FE-23) inspecionam storage e cookie.
    files: ['**/*.spec.ts', 'e2e/**/*.ts'],
    rules: { 'no-restricted-globals': 'off', 'no-restricted-syntax': 'off', 'no-console': 'off' },
  },
  {
    files: ['**/*.html'],
    extends: [angular.configs.templateRecommended, angular.configs.templateAccessibility],
    rules: {},
  },
]);
