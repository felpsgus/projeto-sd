# BE-30 — Configuração por ambiente dos endereços e portas

| | |
|---|---|
| **Domínio** | Infraestrutura / Configuração |
| **Serviço** | ambos |
| **Depende de** | [BE-01](BE-01-fundacao-solution.md), [BE-26](BE-26-identity-servidor-grpc.md), [BE-27](BE-27-tasks-cliente-grpc.md) |
| **Bloqueia** | [BE-31](BE-31-verificacao-t1.md) |
| **Regras cobertas** | nenhuma de negócio — implementa a seção 6 de `CONVENCOES-CODIGO.md` (nenhum segredo, nenhum valor de ambiente no código) |
| **Estimativa** | P |

## Objetivo

Onde cada serviço escuta e para onde o Tasks chama são **configuração**, não código: mover os serviços para outro host, outra porta ou outro ambiente é editar `appsettings` ou definir variáveis de ambiente, nunca recompilar.

## Escopo

### Inclui

- Seções de configuração tipadas com `IOptions<T>` e `ValidateOnStart` ([BE-01](BE-01-fundacao-solution.md)):

  **Tasks Service** — `IdentityOptions`:

  | Chave | Tipo | Padrão em dev | Obrigatória |
  |---|---|---|---|
  | `Identity:GrpcAddress` | `string` (URI absoluta) | `http://localhost:5081` | **sim** |
  | `Identity:GrpcTimeoutSeconds` | `int` (> 0) | `2` | não |

  **Identity Service** — endpoints Kestrel:

  | Endpoint | Padrão em dev | Protocolo |
  |---|---|---|
  | REST | `http://localhost:5080` | `Http1` |
  | gRPC | `http://localhost:5081` | **`Http2`** |

- Sobrescrita por **variável de ambiente**, com o separador de dois níveis do .NET (`__`): `Identity__GrpcAddress`, `Identity__GrpcTimeoutSeconds`, `ASPNETCORE_URLS` / `Kestrel__Endpoints__*`. Nenhuma chave nova precisa ser inventada para isso funcionar — é o comportamento padrão do provedor de configuração, e é ele que permitirá injetar os endereços pelo painel do provedor de nuvem mais adiante.
- `appsettings.json` com os valores neutros, `appsettings.Development.json` com os valores locais. **Nenhum** dos dois contém segredo.
- Validação na inicialização: `Identity:GrpcAddress` ausente, vazio ou não parseável como URI absoluta **derruba a inicialização** do Tasks com mensagem clara ([BE-01](BE-01-fundacao-solution.md), CA-08).
- Seção **"Configuração"** no `README.md` da raiz: tabela de todas as chaves, valor padrão, se é obrigatória e o nome da variável de ambiente equivalente.

### Não inclui

- Containerização, imagens ou implantação em nuvem — outra etapa. Aqui apenas se garante que **nada** impeça a injeção de endereços por variável de ambiente quando essa etapa chegar.
- Descoberta de serviço, service mesh ou balanceamento. O endereço é um valor de configuração, direto.
- TLS entre os serviços. Em desenvolvimento a comunicação é h2c ([BE-26](BE-26-identity-servidor-grpc.md)); TLS é decisão da etapa de implantação.

## Notas técnicas

- **Por que isso é uma task e não um detalhe.** Um endereço hardcoded não dá erro de compilação, não quebra teste local e só aparece na primeira tentativa de rodar em outro ambiente — quando é tarde. O custo de tratá-lo agora é uma seção de `appsettings`; depois, é caçar literais espalhados.
- **`GrpcAddress` é URI completa, não host + porta separados.** O esquema importa: `http://` e `https://` mudam o comportamento do canal gRPC. Partir o valor em pedaços obrigaria a remontá-lo e a decidir o esquema no código.
- **O endpoint gRPC do Identity precisa ser declarado `Http2` explicitamente.** Sem TLS, o Kestrel não consegue negociar o protocolo por ALPN e o padrão (`Http1AndHttp2`) resolve para HTTP/1.1 — o cliente gRPC então falha com um erro de protocolo pouco óbvio. Declarar o endpoint como `Http2` é a solução; o switch `System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport` do lado cliente é remendo, e fica apenas documentado como último recurso ([BE-31](BE-31-verificacao-t1.md)).
- **Portas separadas para REST e gRPC no Identity** porque os protocolos diferem: um endpoint `Http2` puro não atende bem um navegador, e um `Http1AndHttp2` sem TLS não atende gRPC. Dois endpoints resolvem sem ambiguidade.
- Valores de `appsettings.Development.json` são conveniência local, não contrato: os testes de integração **NÃO DEVEM** depender das portas fixas — usam a porta efêmera que a `WebApplicationFactory` atribui.

## Critérios de aceite

- [x] **CA-01** — Uma varredura do código-fonte (`src/`) não encontra `localhost`, `127.0.0.1`, `http://`, `https://` nem número de porta literal fora de `appsettings*.json` e do `Program.cs` de bootstrap. Verificado manualmente (varredura textual de `src/` inteiro) e reforçado por teste já existente do lado do Tasks (`ArchitectureTests.CodigoDoTasks_NaoContemEnderecoOuPortaLiteral_ForaDosAppsettings`, de BE-27). **Nota:** `TasksDbContextFactory`/`IdentityDbContextFactory` (design-time, `dotnet ef`) têm um fallback `127.0.0.1` para a connection string local do `docker-compose.yml` — é conexão com Postgres (BE-02), não endereço/porta do Identity/Tasks/gRPC (fora das duas tabelas desta task), já documentado no próprio código como exceção deliberada; nenhuma outra ocorrência encontrada.
- [x] **CA-02** — `Identity:GrpcAddress` ausente ou inválido **derruba a inicialização** do Tasks com mensagem nomeando a chave — não falha na primeira requisição. `IdentityGrpcOptions`/`AddIdentityGrpcClient` ganharam validação de URI absoluta com mensagem citando `Identity:GrpcAddress`; testes em `IdentityGrpcOptionsValidationTests` (`TodoList.Tasks.UnitTests`).
- [x] **CA-03** — Definir `Identity__GrpcAddress` como variável de ambiente sobrescreve o valor do `appsettings`, sem recompilar ([BE-27](BE-27-tasks-cliente-grpc.md), CA-07). Teste com variável de ambiente real (não só configuração em memória) em `GrpcIdentityGatewayIntegrationTests`.
- [x] **CA-04** — `Identity__GrpcTimeoutSeconds=1` altera de fato o deadline das chamadas, comprovado por teste. Idem, com um `IUserLookup` deliberadamente lento (10s) provando que o deadline efetivo é o 1s da variável de ambiente.
- [x] **CA-05** — `Identity:GrpcTimeoutSeconds` igual a zero ou negativo é rejeitado na inicialização. Coberto por `[Range(1, int.MaxValue)]` (já existente) + testes em `IdentityGrpcOptionsValidationTests`.
- [ ] **CA-06** — Subir os dois serviços em portas diferentes das padrão, apenas por variável de ambiente, funciona de ponta a ponta: `POST /api/tasks` cria a tarefa passando pelo Identity no novo endereço. **Parcialmente automatizado**: `CreateTaskCustomAddressEndToEndTests` prova o caminho completo (`POST /api/tasks` → gRPC → Identity) com o Identity em endereço não padrão, mas sobre `WebApplicationFactory`/`TestServer` (sem porta TCP real). O cenário com dois processos `dotnet run` reais em portas diferentes fica como **roteiro manual documentado no README** (seção "Configuração") — não verificado nesta sessão contra processos reais, por isso a caixa segue desmarcada.
- [x] **CA-07** — O endpoint gRPC do Identity está declarado como `Http2` na configuração, não herdando o padrão. Já estava correto em `appsettings.json`/`appsettings.Development.json` do Identity antes desta task; confirmado por leitura.
- [x] **CA-08** — Nenhum `appsettings*.json` versionado contém segredo, senha ou connection string real ([BE-02](BE-02-persistencia-base.md), CA-11). Confirmado por leitura de todos os `appsettings*.json` dos dois serviços — nenhum declara `ConnectionStrings`.
- [x] **CA-09** — A seção "Configuração" do `README.md` lista **todas** as chaves das duas tabelas acima, com padrão, obrigatoriedade e variável de ambiente equivalente. Tabelas adicionadas, mais uma terceira com as demais chaves de endereço/porta/ambiente já existentes na base.
- [x] **CA-10** — Os testes de integração passam sem depender das portas fixas de desenvolvimento. Confirmado por varredura de `tests/` (nenhum `5080`/`5081`/`5100` literal em código-fonte de teste, só em `bin/`/`obj/`) — todos usam a porta efêmera de `WebApplicationFactory`.

## Testes obrigatórios

- Integração: CA-02, CA-05 — subir com configuração faltando/inválida e afirmar a falha na inicialização.
- Integração: CA-03, CA-04, CA-06 — sobrescrita por variável de ambiente com efeito observável.
- Verificação automatizada no CI para CA-01 (varredura por padrão de literal), junto das varreduras já previstas em [BE-24](BE-24-observabilidade-ci.md).
