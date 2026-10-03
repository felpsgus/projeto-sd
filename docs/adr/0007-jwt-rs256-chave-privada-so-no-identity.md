# ADR-0007 — JWT RS256: chave privada só no Identity, validação local no Gateway

- **Status:** aceita (21/09/2026) — decisão **D-38**, que **substitui D-31** (HS256 + `ValidateToken`); ver
  `tarefas/backend/DECISOES-PENDENTES.md`
- **Contexto de regra:** RN-AUTH-10, RN-AUTH-11; tasks BE-08, BE-34, BE-40

## Contexto

D-31 mantinha a chave de assinatura (HS256, simétrica) só no Identity, e o Gateway validava o token perguntando
por gRPC (`ValidateToken`) a cada requisição. O enunciado do T2 passou a exigir **middleware de JWT no próprio
Gateway**. Com HS256, quem valida também assina: distribuir a chave promoveria o Gateway a emissor.

## Decisão

- **RS256.** `Jwt:PrivateKeyPath` (PEM PKCS8, RSA ≥ 2048 bits) existe **só no Identity**, que assina.
  `Jwt:PublicKeyPath` (PEM SPKI) existe **só no Gateway**, que valida localmente com `AddJwtBearer`. O Tasks não
  tem nenhuma chave `Jwt:*`.
- O Gateway não chama o Identity para autenticar. A rota nasce protegida por *fallback policy*;
  `AllowAnonymous` é exceção explícita (`/health`, login, refresh, registro, OpenAPI em Development).
- O Gateway valida assinatura, emissor, audiência e expiração; token com `alg=none`, HS256 ou assinado por outra
  chave é recusado (testes forjam cada caso). O claim `sub` vira `x-user-id` (ADR-0004).
- "Só o Identity emite" continua verdadeiro: a chave pública verifica, não assina.
- `ValidateToken` continua no contrato do Identity, sem consumidor ativo.

## Alternativas consideradas

1. **Manter `ValidateToken` por requisição.** Não cumpre o requisito de validação no Gateway e custa uma ida à
   rede por chamada. Rejeitada.
2. **HS256 com a chave também no Gateway.** Simples, mas quem valida passa a poder forjar tokens. Rejeitada.

## Consequências

- Validar token deixa de depender do Identity: Identity fora do ar não derruba a autenticação, só as rotas que
  precisam dele (503).
- O access token não é revogável antes de expirar (ADR-0001): validação local nunca vê revogação.
- Rotação de chave exige redistribuir a pública ao Gateway; esta versão não tem `kid`/JWKS.
- As chaves vêm de arquivo apontado por variável de ambiente; nenhuma é versionada.
