# FE-12 — Alteração de senha

| | |
|---|---|
| **Domínio** | Autenticação |
| **Depende de** | [FE-11](FE-11-perfil-usuario.md) · backend: [BE-15](../backend/BE-15-alteracao-senha.md) |
| **Bloqueia** | — |
| **Regras cobertas** | RN-AUTH-21, RN-AUTH-04, RN-AUTH-05, RN-AUTH-19 |
| **Estimativa** | M |

## Objetivo

O usuário troca a própria senha informando a atual e a nova — e entende, antes de confirmar, que isso vai desconectar todos os seus dispositivos.

## Escopo

### Inclui

- Rota `/account/password`, protegida por `authGuard`.
- Formulário Signal Forms:

  | Campo | Validação | Regra |
  |---|---|---|
  | Senha atual | obrigatória | RN-AUTH-21 |
  | Nova senha | ≥ 8, ao menos uma letra e um número; diferente da atual | RN-AUTH-04 |
  | Confirmar nova senha | igual à nova | — (UX) |

- Mesmo indicador de requisitos de senha de [FE-08](FE-08-tela-cadastro.md), **reaproveitado** — não recriado.
- **Aviso explícito antes de confirmar**: "Ao alterar a senha, você será desconectado de todos os dispositivos e precisará entrar novamente." (RN-AUTH-19).
- Envio a `POST /api/me/change-password`. Sucesso (204):
  1. encerra a sessão local com motivo `session_revoked`;
  2. leva ao `/login` com mensagem "senha alterada, entre novamente com a nova senha";
  3. e-mail pré-preenchido.
- Erros:
  - senha atual incorreta (`auth.invalid_current_password`) → mensagem junto ao campo **senha atual**;
  - nova senha fora da política (400) → `fieldErrors` no campo correspondente;
  - nova senha igual à atual (400) → mensagem no campo da nova senha.

### Não inclui

- Recuperação de senha esquecida (RN-AUTH-22 / D-04).
- Permanecer logado após a troca — [BE-15](../backend/BE-15-alteracao-senha.md) revoga todas as sessões e não reemite tokens.

## Notas técnicas

- **O redirecionamento ao login após o sucesso não é um bug — é RN-AUTH-19 funcionando.** Sem o aviso prévio (CA-06), o usuário lê como falha: "troquei a senha e o sistema me expulsou". O aviso é o que transforma um comportamento correto em uma experiência compreensível.
- Depois do 204, o access token corrente ainda vale por até 15 minutos ([BE-15](../backend/BE-15-alteracao-senha.md)), mas o refresh já foi revogado. Encerrar a sessão localmente **na hora** evita o estado ambíguo em que a aba parece funcionar até a próxima renovação falhar.
- Diferente de RN-AUTH-09, aqui **é correto** apontar que a senha atual está errada: o usuário já está autenticado, não há informação a proteger.
- `autocomplete`: `current-password` no primeiro campo, `new-password` nos outros dois.
- Validador de política de senha compartilhado com FE-08 — duplicá-lo garante que os dois vão divergir.

## Critérios de aceite

### Fluxo

- [ ] **CA-01** — Troca com senha atual correta e nova senha válida retorna sucesso (RN-AUTH-21).
- [ ] **CA-02** — Após o sucesso, o usuário é levado ao `/login` com mensagem explicando que a senha foi alterada.
- [ ] **CA-03** — Após o sucesso, a sessão local está encerrada: nenhum token em memória ou storage.
- [ ] **CA-04** — O login com a **nova** senha funciona; com a **antiga**, falha.
- [ ] **CA-05** — O e-mail vem pré-preenchido na tela de login.

### Aviso e validação

- [ ] **CA-06** — O aviso sobre desconexão de todos os dispositivos é visível **antes** do envio, não depois (RN-AUTH-19).
- [ ] **CA-07** — Nova senha com 7 caracteres, só letras ou só números é rejeitada com a mensagem do critério que falta (RN-AUTH-04).
- [ ] **CA-08** — O indicador de requisitos atualiza em tempo real e é o **mesmo componente** de FE-08.
- [ ] **CA-09** — Nova senha e confirmação divergentes impedem o envio.
- [ ] **CA-10** — Nova senha igual à atual é rejeitada, com mensagem no campo da nova senha.
- [ ] **CA-11** — Campos vazios impedem o envio.

### Erros

- [ ] **CA-12** — Senha atual incorreta exibe o erro **junto ao campo "senha atual"**, e a sessão **não** é encerrada.
- [ ] **CA-13** — Após esse erro, corrigir e reenviar funciona sem recarregar a página.
- [ ] **CA-14** — Erro 400 do backend é exibido no campo correspondente, não em toast genérico.
- [ ] **CA-15** — Erro de rede exibe mensagem de conectividade e a sessão permanece ativa.

### Segurança e acessibilidade

- [ ] **CA-16** — Nenhuma das três senhas aparece em storage, URL, `console` ou atributo do DOM (teste de segurança).
- [ ] **CA-17** — Os campos usam `autocomplete` correto: `current-password` e `new-password`.
- [ ] **CA-18** — O botão de envio fica desabilitado durante a requisição; clique duplo dispara **uma** chamada.
- [ ] **CA-19** — Labels associados, erros ligados por `aria-describedby`, foco no primeiro campo com erro após falha.
- [ ] **CA-20** — Operável só pelo teclado e usável em 360 px.

## Testes obrigatórios

- Componente (Testing Library): CA-01 a CA-15, CA-18, CA-19.
- **CA-16 é teste de segurança obrigatório.**
- CA-04 é verificado em E2E ([FE-22](FE-22-testes-e2e.md)), pois cruza duas telas e o backend.
