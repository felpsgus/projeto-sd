# BE-31 — Verificação da comunicação gRPC: roteiro e critérios de aceite

| | |
|---|---|
| **Domínio** | Qualidade / Documentação |
| **Serviço** | ambos |
| **Depende de** | [BE-25](BE-25-contrato-grpc-identity.md) a [BE-30](BE-30-configuracao-enderecos-grpc.md) |
| **Bloqueia** | — (fecha a etapa) |
| **Regras cobertas** | RN-AUTZ-01, RN-USER-04, RN-TASK-10 (verificação de ponta a ponta) |
| **Estimativa** | P |

## Objetivo

Qualquer pessoa consegue, seguindo apenas o `README.md`, subir os dois serviços, ver uma tarefa ser criada depois de o Identity confirmar o dono por gRPC, e ver a criação ser recusada quando o Identity nega — com o log dos dois lados evidenciando a ida e a volta.

## Escopo

### Inclui

- Seção **"Rodando os dois serviços"** no `README.md` da raiz, contendo:

  **Pré-requisitos e subida**
  - o que precisa estar instalado, como subir o PostgreSQL do `docker-compose` e aplicar as migrations dos dois serviços **na ordem correta — Identity primeiro**, porque a FK do Tasks depende de `identity.users` ([BE-02](BE-02-persistencia-base.md));
  - os dois comandos de execução, e a ordem: **Identity primeiro**, Tasks depois;
  - os ids dos usuários de demonstração — um ativo, um inativo — sejam eles do seed em memória ou criados por `POST /api/auth/register` ([BE-26](BE-26-identity-servidor-grpc.md), CA-14).

  **Caminho de sucesso**
  - `POST /api/tasks` com `X-User-Id` de usuário existente e ativo ([BE-29](BE-29-gatilho-http-criar-tarefa.md));
  - o Tasks chama `ValidateUser` no Identity via gRPC;
  - o Identity responde `exists=true, active=true`;
  - a tarefa é gravada e a resposta é **201** com o DTO completo da tarefa.

  **Caminho de falha — dono inexistente**
  - `POST /api/tasks` com `X-User-Id` de usuário que não existe no Identity;
  - o Identity responde `exists=false`;
  - a criação é **rejeitada com 404** e `task.owner_not_found`, **por causa da resposta gRPC** — não de validação local no Tasks ([BE-28](BE-28-validacao-dono-grpc.md), CA-05).

  **Caminho de falha — Identity fora do ar**
  - com o Identity desligado, `POST /api/tasks` responde **503** com `identity.unavailable`, e nada é gravado (**D-28**).

  Cada caminho com o comando `curl` (ou `.http`) pronto para copiar e a resposta esperada.

- **Evidência de log:** para cada caminho, o trecho de log esperado dos **dois** serviços, correlacionados pelo mesmo `traceId` — a saída do Tasks registrando a chamada e a do Identity registrando o `ValidateUser` recebido ([BE-24](BE-24-observabilidade-ci.md), CA-07c).
- **Nota sobre HTTP/2 sem TLS (h2c).** Com o Identity em `http://`, o endpoint Kestrel **DEVE** estar declarado como `HttpProtocols.Http2` ([BE-30](BE-30-configuracao-enderecos-grpc.md), CA-07). Sem isso o canal negocia HTTP/1.1 e o cliente falha com um erro de protocolo pouco informativo. O switch `System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport` **NÃO DEVE** ser a solução adotada; fica registrado no README apenas como alternativa de último recurso, com a razão da preferência.
- **Sintomas e causas** dos erros mais prováveis de quem está subindo pela primeira vez: recusa de conexão (Identity não subiu ou porta errada), erro de protocolo (endpoint não é `Http2`), 404 inesperado (id de usuário não existe no Identity), 503 (Identity inalcançável).
- **Teste de integração ponta a ponta** cobrindo os três caminhos, com os dois serviços reais — o roteiro escrito não substitui verificação automatizada.

### Não inclui

- API Gateway, autenticação na borda ou resposta **401** — etapa seguinte.
- Containerização e implantação em nuvem — etapa seguinte.
- Qualquer alteração no frontend.
- Novos comportamentos: esta task **documenta e verifica** o que BE-25 a BE-30 produziram; se algo não funciona, o defeito é da task de origem, não daqui.

## Notas técnicas

- **Por que a ordem de subida importa.** O Tasks não falha ao iniciar com o Identity fora do ar — ele só falha na primeira criação, com 503 (**D-28**). Documentar a ordem evita que isso seja lido como bug.
- **Por que o caminho de falha entra no roteiro.** Um sucesso isolado não prova que a decisão veio do outro serviço: um Tasks que aceitasse tudo passaria igual. O par sucesso/rejeição, com a mesma requisição mudando apenas o `X-User-Id`, é o que demonstra que quem decide é o Identity.
- **Por que o log é entregável.** A serialização gRPC é binária: sem log correlacionado, não há nada observável entre "requisição entrou" e "resposta saiu". As duas entradas com o mesmo `traceId` são a evidência de que houve ida e volta pela rede, e não uma chamada de método local.
- O roteiro **DEVE** ser executado por alguém que não escreveu o código, em máquina limpa, antes de a task fechar. Um roteiro validado só pelo autor tende a omitir o passo que ele faz no automático.

## Critérios de aceite

- [x] **CA-01** — A seção "Rodando os dois serviços" existe no `README.md` da raiz e cobre os três caminhos. Inclui um quarto desfecho (dono inativo → **409**), que é a terceira resposta possível da mesma chamada `ValidateUser`.
- [ ] **CA-02** — Uma pessoa que nunca viu o projeto sobe os dois serviços seguindo **apenas** o README, em máquina limpa, sem consultar o código. **Não auto-certificável:** o roteiro foi executado pelo autor, na máquina de desenvolvimento. Falta a passada de alguém que não escreveu o código — registrar o resultado no PR (ver "Testes obrigatórios").
- [x] **CA-03** — Caminho de sucesso: `POST /api/tasks` com usuário existente e ativo responde **201** com a tarefa criada. Verificado contra os dois processos reais (resposta literal no README) e automatizado em `CreateTaskOwnerValidationTests`.
- [x] **CA-04** — Caminho de falha: a **mesma** requisição, mudando apenas o `X-User-Id` para um usuário inexistente, responde **404** com `task.owner_not_found`. Idem — real e automatizado.
- [x] **CA-05** — A rejeição do CA-04 é atribuível à resposta do Identity: alterando o Identity para reconhecer aquele usuário, a mesma requisição passa a responder **201** — sem tocar no Tasks. Automatizado em `CreateTaskOwnerValidationTests.PostTasks_MesmoUsuarioPassaAExistir_RejeicaoDesaparece_SemMudancaNoTasks` e, com dois Identity distintos, em `GrpcIdentityGatewayIntegrationTests`.
- [x] **CA-06** — Caminho de indisponibilidade: com o Identity desligado, a requisição responde **503** com `identity.unavailable` e nada é gravado no banco. Verificado com o Identity encerrado de verdade (`Retry-After: 5`, nenhuma linha nova em `tasks.tasks`) e automatizado em `CreateTaskOwnerValidationTests`.
- [x] **CA-07** — Em cada caminho, o log do Tasks mostra a chamada de saída (`ValidateUser`, `userId`, duração, `StatusCode`) e o log do Identity mostra a chamada recebida com o resultado — **com o mesmo `traceId`**. O lado do Tasks não logava `traceId` (só o Identity logava); o campo foi adicionado a `GrpcIdentityGateway` para fechar a correlação. Automatizado em `TraceIdCorrelationTests` (sucesso e rejeição) e conferido nos dois consoles reais.
- [x] **CA-08** — Os comandos `curl` do README funcionam copiados e colados, sem edição além do id do usuário. Executados no PowerShell 7, na forma exata do README (continuação por crase), contra os dois serviços no ar.
- [x] **CA-09** — A nota sobre h2c está no README, indicando `HttpProtocols.Http2` como a configuração adotada e explicando por que o switch do lado cliente não é a solução preferida.
- [x] **CA-10** — A seção de sintomas e causas cobre os quatro erros listados no escopo (recusa de conexão, erro de protocolo, 404 inesperado, 503), mais dois observados na execução real: **400** por `X-User-Id` ausente/malformado e falha de FK quando o Identity aprova um dono que não está em `identity.users`.
- [x] **CA-11** — Existe teste de integração automatizado cobrindo CA-03, CA-04 e CA-06 com os dois serviços reais. `CreateTaskOwnerValidationTests` sobe o Identity real em `WebApplicationFactory` e fala com ele pelo cliente gRPC real; o cenário de indisponibilidade aponta para um endereço de rede morto.
- [x] **CA-12** — Nenhum log exibido no README contém dado sensível — sem token, senha ou e-mail ([BE-24](BE-24-observabilidade-ci.md)). As linhas mostram apenas `userId`, `exists`/`active`, duração e `traceId`.

## Testes obrigatórios

- Integração ponta a ponta: CA-03, CA-04, CA-06 — Tasks e Identity reais, não substituídos.
- Verificação manual documentada de CA-02, feita por alguém que não escreveu o código, com o resultado registrado no PR.
