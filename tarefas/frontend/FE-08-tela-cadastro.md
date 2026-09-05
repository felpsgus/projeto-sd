# FE-08 — Tela de cadastro

| | |
|---|---|
| **Domínio** | Autenticação |
| **Depende de** | [FE-07](FE-07-roteamento-guards.md) · backend: [BE-07](../backend/BE-07-cadastro-usuario.md) |
| **Bloqueia** | — |
| **Regras cobertas** | RN-AUTH-01 a RN-AUTH-05, RN-AUTH-07 |
| **Estimativa** | M |

## Objetivo

Um visitante cria a própria conta em `/register`, com validação imediata e mensagens claras sobre o que precisa corrigir.

## Escopo

### Inclui

- Rota `/register` no `AuthLayout`, protegida por `guestGuard`.
- Formulário com **Signal Forms** (FD-11):

  | Campo | Validação no cliente | Regra |
  |---|---|---|
  | E-mail | obrigatório, formato válido | RN-AUTH-03 |
  | Senha | ≥ 8 caracteres, ao menos uma letra e um número | RN-AUTH-04 |
  | Confirmar senha | igual à senha | — (só UX) |
  | Nome de exibição | opcional, ≤ 100 caracteres | RN-AUTH-07 |

- **Indicador de requisitos de senha** ao vivo: os três critérios de RN-AUTH-04 listados, cada um marcado conforme atendido. Nada de "força da senha" genérica — mostrar exatamente o que a regra exige.
- Dica visível de que, sem nome de exibição, será usada a parte do e-mail antes do `@` (RN-AUTH-07).
- Envio a `POST /api/auth/register`; sucesso → mensagem de confirmação e redirecionamento para `/login` com o e-mail pré-preenchido.
- Tratamento de erros:
  - **409** `auth.email_already_registered` → mensagem junto ao campo de e-mail, com link para o login;
  - **400** → `fieldErrors` de [FE-03](FE-03-erros-feedback.md) exibidos nos campos correspondentes;
  - demais → toast genérico.
- Campos de senha com botão de **mostrar/ocultar**, acessível.
- Link para "já tenho conta".

### Não inclui

- Autenticar automaticamente após o cadastro — [BE-07](../backend/BE-07-cadastro-usuario.md) não emite tokens no cadastro.
- Confirmação de e-mail, convite (fora do escopo, seção 8 das regras).

## Notas técnicas

- **A validação no cliente é conveniência, não autoridade** (FD-14). Ela espelha RN-AUTH-03/04 para dar retorno imediato; o backend continua sendo quem decide. Se as duas divergirem, a mensagem do backend prevalece na tela.
- **"Confirmar senha" não é regra de negócio** — não está em nenhuma RN. É proteção contra erro de digitação num campo mascarado, e vale a pena porque não há recuperação de senha nesta versão (RN-AUTH-22): quem errar a senha no cadastro perde a conta.
- **RN-AUTH-05 no frontend significa:** a senha não vai para `localStorage`, não entra em log, não aparece em URL, e o campo tem `autocomplete="new-password"` para o gerenciador do navegador tratá-la corretamente.
- O 409 de e-mail duplicado **pode** ser exibido explicitamente — RN-AUTH-09 fala de *login*, não de cadastro, e o usuário precisa saber que já tem conta.
- `autocomplete`: `email` no e-mail, `new-password` nas senhas, `name` no nome.

## Critérios de aceite

### Formulário

- [ ] **CA-01** — Cadastro com dados válidos leva ao `/login` com mensagem de sucesso e o e-mail pré-preenchido.
- [ ] **CA-02** — O botão de envio fica desabilitado enquanto o formulário é inválido e enquanto o envio está em andamento.
- [ ] **CA-03** — Clique duplo no botão de envio dispara **uma** requisição.
- [ ] **CA-04** — E-mail em formato inválido exibe erro junto ao campo, antes do envio (RN-AUTH-03).
- [ ] **CA-05** — Senha com 7 caracteres, só letras, ou só números exibe o erro específico do critério que falta (RN-AUTH-04).
- [ ] **CA-06** — O indicador de requisitos marca cada critério em tempo real conforme o usuário digita.
- [ ] **CA-07** — Senha e confirmação diferentes impedem o envio e mostram a divergência.
- [ ] **CA-08** — Nome de exibição com 101 caracteres é rejeitado; com 100 é aceito.
- [ ] **CA-09** — Deixar o nome vazio é permitido, e a tela informa que será usado o trecho antes do `@` (RN-AUTH-07).
- [ ] **CA-10** — Mensagens de erro só aparecem depois que o campo foi tocado — não ao abrir a tela.

### Integração e erros

- [ ] **CA-11** — E-mail já cadastrado (409) exibe a mensagem junto ao campo de e-mail, com link para o login (RN-AUTH-02).
- [ ] **CA-12** — Erros 400 do backend são exibidos nos campos correspondentes, não num toast genérico.
- [ ] **CA-13** — Erro de rede exibe mensagem de conectividade e **preserva** os dados já digitados (exceto senhas).
- [ ] **CA-14** — Após um erro, corrigir e reenviar funciona sem recarregar a página.

### Segurança e acessibilidade

- [ ] **CA-15** — Nenhuma senha aparece em `localStorage`, `sessionStorage`, URL, `console` ou atributo do DOM (teste automatizado).
- [ ] **CA-16** — Os campos de senha usam `type="password"` e `autocomplete="new-password"`.
- [ ] **CA-17** — O botão mostrar/ocultar senha tem rótulo acessível que reflete o estado atual.
- [ ] **CA-18** — Todo campo tem `<label>` associado; erros são ligados por `aria-describedby` e o campo inválido tem `aria-invalid`.
- [ ] **CA-19** — O formulário é preenchível e enviável apenas pelo teclado.
- [ ] **CA-20** — Ao falhar o envio, o foco vai para o primeiro campo com erro (ou para o resumo de erros).
- [ ] **CA-21** — A tela é usável em 360 px de largura.

## Testes obrigatórios

- Componente (Testing Library, consultando por label e texto): CA-01 a CA-14, CA-17 a CA-20.
- **CA-15 é teste de segurança obrigatório.**
- O fluxo de cadastro entra no E2E crítico de [FE-22](FE-22-testes-e2e.md).

## Decisões em aberto

- **FD-11** — Signal Forms.
- **FD-14** — Validação no cliente espelhando a política do backend.
