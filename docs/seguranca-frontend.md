# Segurança do frontend (FE-23)

## Cabeçalhos de segurança

O app é servido por `frontend/nginx.conf.template` (nginx do container `frontend`, origem única
para estático + `/api/`). Os quatro cabeçalhos abaixo já estão aplicados lá — nas respostas de
`/`, de `/index.html` e dos assets com hash (o nginx não herda `add_header` entre níveis, por
isso são repetidos em cada `location` que declara `Cache-Control`).

| Cabeçalho | Valor | Por quê |
|---|---|---|
| `Content-Security-Policy` | ver abaixo | Limita de onde o documento pode carregar script/estilo/etc. |
| `X-Content-Type-Options` | `nosniff` | Impede o navegador de "adivinhar" MIME e executar JSON/texto como script |
| `Referrer-Policy` | `strict-origin-when-cross-origin` | Não vaza caminho/query para outras origens |
| `X-Frame-Options` | `SAMEORIGIN` | Anti-clickjacking para navegadores sem `frame-ancestors`; coerente com a CSP |

### CSP aplicada (e validada pela suíte E2E)

```
default-src 'self';
script-src 'self';
style-src 'self' 'unsafe-inline';
img-src 'self' data:;
font-src 'self' data:;
connect-src 'self';
object-src 'none';
base-uri 'self';
form-action 'self';
frame-ancestors 'self'
```

- `script-src 'self'`: o build de produção só tem `<script src>` com hash — sem `eval`, sem inline.
- `style-src 'unsafe-inline'`: o Angular injeta o CSS de cada componente como `<style>` em
  runtime. É a única relaxação; não há `unsafe-eval` em lugar nenhum. Endurecimento possível
  (não feito): nonce por requisição (`ngCspNonce`) gerado pelo nginx/gateway — exige geração
  dinâmica do `index.html`, fora do escopo atual.
- `connect-src 'self'`: a API é a mesma origem (`/api/*`, sem CORS).
- Fontes (`@fontsource`) são servidas pelo próprio app, nunca por CDN.

**Efeito colateral encontrado e corrigido:** com a otimização padrão do build ("inline critical
CSS"), o Angular emite `<link rel="stylesheet" media="print" onload="this.media='all'">`. O
`onload` inline é bloqueado por `script-src 'self'`, e o CSS global nunca era aplicado em
produção (o skip link ficava sempre visível, por exemplo). A correção foi
`optimization.styles.inlineCritical: false` no `angular.json`. O E2E (`e2e/leak.spec.ts` e
`a11y.spec.ts`, rodando contra o nginx com a CSP ligada) cobre a regressão: o skip link e os
alvos de toque só passam com o CSS aplicado. (Não há asserção específica contra mensagens de
violação de CSP no `console`.)

### Fora do nginx

- **HSTS** (`Strict-Transport-Security`) e TLS dependem de quem termina HTTPS (Cloud Run / load
  balancer na T3) — não foram configurados aqui. O cookie de refresh usa `Secure` por padrão
  (`RefreshCookie:Secure`); `http://localhost` é contexto seguro, qualquer outro host em HTTP
  puro exige `RefreshCookie__Secure=false`.
- `Permissions-Policy`: não definido (sem uso de câmera/geolocalização etc.).

## Sessão e dados sensíveis no cliente

- Access token só em memória; refresh token só em cookie `HttpOnly; SameSite=Strict; Path=/api/auth`.
- Regras de lint proíbem `localStorage`, `sessionStorage`, `document.cookie` e identificadores
  `refreshToken` fora de testes (`eslint.config.js`).
- `e2e/leak.spec.ts` (FE-23 CA-15): cadastro -> login -> recarga (refresh) -> troca de senha ->
  login -> exclusão de conta, com checagem em cada etapa de que **senha(s), access tokens e o valor
  do cookie de refresh** não aparecem em storage, `document.cookie`, URLs (navegações e
  requisições), mensagens de `console`/erros de página nem no DOM serializado, e de que o cookie
  `refreshToken` é `HttpOnly`.

## Build

- Produção não gera sourcemaps (nenhum `.map` em `dist/`); se forem necessários para rastreio de
  erro, gerar à parte e **não** copiar para a imagem do nginx.
- Orçamento de bundle inicial: aviso 320 kB, erro 400 kB (atual: ~295 kB). Estourar o erro
  falha o build (comprovado em 03/10/2026 baixando o teto temporariamente).
- Varredura de dependências: `npm audit --audit-level=high`. Resultado de 03/10/2026 em
  `frontend/`: **falha** — `@angular/router >=22.0.0 <22.2.0` (alta; DoS via parâmetros de matriz
  numéricos, afeta apenas SSR, que o app não usa) e `piscina 5.0.0-5.3.1` (crítica; RCE por
  `ThreadPool.options`, dependência de build de `@angular/build`, não vai para o bundle). Correção
  exige atualizar o Angular (`npm audit fix`); **não aplicada** nesta rodada.
