# Cloud SQL for PostgreSQL — runbook de uso (corrigido em 30/09/2026)

Runbook para colocar o Postgres do roteiro de 22/10 fora da VM, no Cloud SQL for
PostgreSQL (projeto `sd-26-2`, `us-central1`), na mesma VPC de `maquina-1-psd`
(`10.128.0.4`, ver `deploy/README.md`).

> **Correção de 30/09/2026 — a instância já existe; não crie nada.** A versão
> original deste arquivo (escrita na Onda C, nunca executada) mandava criar uma
> instância `todolist-cloudsql` e, antes dela, o peering de Private Services
> Access. Uma consulta de leitura ao projeto `sd-26-2` mostrou que **os dois já
> estão prontos**: existe a instância **`banco-1`** (PostgreSQL 18, parada) com
> IP privado na VPC `default`, e o peering com o bloco reservado. O runbook
> abaixo foi reescrito para **usar** o que existe; o caminho de criação ficou na
> seção 8, como referência para o caso de um dia precisar de outra instância.
>
> O que **ainda** nunca foi executado: ligar a instância, conferir/criar o banco
> e o usuário da aplicação, e conectar a stack a ela. Trate o primeiro ensaio
> com folga.

## Estado real do projeto (lido em 30/09/2026, só comandos de leitura)

| Item | Valor |
|---|---|
| Instância | **`banco-1`** — `POSTGRES_18`, região `us-central1`, zonal |
| Estado | **`STOPPED`** (`activationPolicy: NEVER`) |
| Tier | `db-custom-2-8192` (2 vCPU, 8 GB) — bem acima do necessário, ver seção 7 |
| Disco | 10 GB |
| **IP privado** | **`10.30.240.3`** — é este que entra no `.env` da VM |
| IP público | `34.67.193.216` (`ipv4Enabled: true`), rede autorizada `200.137.197.75` |
| IP de saída | `35.226.15.84` |
| VPC do IP privado | `projects/sd-26-2/global/networks/default` — a mesma de `maquina-1-psd` |
| Peering (PSA) | `default-ip-range-1790293144149`, `10.30.240.0/20`, `VPC_PEERING`, `RESERVED` |
| TLS | **`sslMode: ENCRYPTED_ONLY`** — conexão sem TLS é **rejeitada** |
| CA do servidor | `GOOGLE_MANAGED_INTERNAL_CA` (CA própria da instância) |

Duas consequências que mudam o que o runbook antigo dizia:

- **A seção 1 do texto original (criar o peering) está cumprida.** O bloco
  `10.30.240.0/20` já está reservado e conectado na VPC `default`, e o IP privado
  da instância (`10.30.240.3`) cai dentro dele. Nada a fazer.
- **`SSL Mode=Require` deixou de ser "a opção pragmática" e passou a ser o
  mínimo.** Com `sslMode: ENCRYPTED_ONLY`, uma connection string sem TLS não
  conecta — não é escolha de rigor, é requisito. O
  `deploy/todolist.env.example` já traz `SSL Mode=Require` nas duas connection
  strings e `PGSSLMODE=require` para o `psql`, então **nada precisa mudar** ali;
  o que muda é o motivo. `VerifyFull` continua sendo o alvo (seção 5).

## 0. Pré-requisitos antes do primeiro comando

- `gcloud` autenticado no projeto certo: `gcloud config set project sd-26-2`
  (confirmado em 30/09/2026: `gcloud` 584.0.0 local, projeto `sd-26-2` ativo).
- A VPC **é a `default`** — o projeto tem uma rede só, modo automático,
  confirmado por `gcloud compute networks list`. O passo de "descubra qual é a
  VPC" do texto original não é mais necessário; a resposta está acima.

## 1. Ligar a instância

A `banco-1` está com `activationPolicy: NEVER`, que é como o Cloud SQL
representa "parada". Não existe `gcloud sql instances start` — quem liga é um
`patch` da política de ativação:

```bash
gcloud sql instances patch banco-1 \
  --project=sd-26-2 \
  --activation-policy=ALWAYS
```

Demora alguns minutos. Enquanto a instância está parada, **nem `gcloud sql
databases list` nem `gcloud sql users list` funcionam** — os dois respondem
`HTTPError 400: Invalid request since instance is not running` (foi exatamente o
que aconteceu na consulta de 30/09/2026). É por isso que o passo 2 vem depois
deste, e não antes.

## 2. Conferir — e só então criar — o banco e o usuário da aplicação

**Não se sabe ainda** se a `banco-1` já tem o banco `todolist` e o usuário
`todolist`: ela é anterior a este trabalho e não deu para listar com a instância
parada. Portanto, com a instância no ar, **primeiro liste**:

```bash
gcloud sql databases list --instance=banco-1 --project=sd-26-2
gcloud sql users list     --instance=banco-1 --project=sd-26-2
```

Se o banco não existir:

```bash
gcloud sql databases create todolist --instance=banco-1 --project=sd-26-2
```

Se o usuário não existir (use `--prompt-for-password` para a senha não ficar no
histórico do shell):

```bash
gcloud sql users create todolist \
  --instance=banco-1 \
  --project=sd-26-2 \
  --prompt-for-password
```

Se o usuário **já existir** com senha desconhecida, redefina em vez de criar:

```bash
gcloud sql users set-password todolist \
  --instance=banco-1 \
  --project=sd-26-2 \
  --prompt-for-password
```

Essa senha é a mesma que entra em `PGPASSWORD` e nas duas
`ConnectionStrings__*Db` do `.env` real na VM (`deploy/todolist.env.example`) —
e em nenhum arquivo versionado.

> **Atenção, a instância não é exclusiva deste trabalho.** A `banco-1` existe no
> projeto desde antes desta onda e pode ter banco, usuário e dados de outra
> atividade da disciplina. Por isso este runbook **lista antes de criar** e
> **nunca** manda apagar a instância (o texto original recomendava `gcloud sql
> instances delete` depois da demonstração — ver seção 7: isso não vale mais).

## 3. Onde ler o IP privado

Já lido e registrado: **`10.30.240.3`**. Para reconferir (a instância precisa
estar no ar para o `describe` trazer tudo, mas o IP aparece mesmo parada):

```bash
gcloud sql instances describe banco-1 \
  --project=sd-26-2 \
  --format="value(ipAddresses.filter(\"type:PRIVATE\").extract(\"ipAddress\"))"
```

Esse valor é o que substitui `<IP_PRIVADO_CLOUDSQL>` nas duas connection strings
e em `PGHOST` de `deploy/todolist.env.example`, no `.env` real da VM (nunca
commitado).

**Use o IP privado, não o público.** A instância tem um IP público
(`34.67.193.216`) com uma rede autorizada `200.137.197.75` — pelo nome da
entrada (`maquina-1-psd`), alguém a cadastrou achando que era o IP da VM; é um
endereço externo que não corresponde a nenhum recurso atual do projeto e que,
se for a saída de uma rede doméstica ou do campus, muda sem aviso. A VM alcança
a instância pelo caminho privado, dentro da VPC, sem depender dessa lista.

## 4. Versão do servidor: 18 aqui, 17 nos containers

A instância é **PostgreSQL 18**. O compose de desenvolvimento
(`docker-compose.yml`) e o serviço `migrate` de `deploy/docker-compose.prod.yml`
usam a imagem **`postgres:17-alpine`**.

- **No `migrate`, isso é só o cliente `psql`.** Um `psql` 17 aplicando DDL num
  servidor 18 é um cenário suportado — libpq é compatível com servidores mais
  novos, e os dois arquivos aplicados (`artifacts/sql/01-identity.sql` e
  `02-tasks.sql`) são DDL gerado pelo EF Core, sem recurso de versão. Risco
  baixo, mas **não testado contra a `banco-1`**.
- **Mesmo assim, vale alinhar.** Subir o `migrate` para `postgres:18-alpine`
  elimina a divergência de major entre quem aplica a migration e quem a recebe,
  pelo custo de uma linha. Fica registrado como ajuste recomendado em
  `deploy/docker-compose.prod.yml` — não aplicado aqui, porque este arquivo é
  runbook, não configuração.
- O argumento do texto original ("não divirja de major entre dev e produção")
  continua de pé; o que mudou é que a divergência já existe e veio da instância
  pronta, não de uma escolha desta onda.

## 5. `VerifyFull` — o que ainda falta

`deploy/todolist.env.example` documenta duas formas de TLS: a usada hoje
(`SSL Mode=Require` — cifra o canal, não valida a cadeia nem protege contra
man-in-the-middle) e o alvo (`SSL Mode=VerifyFull`).

> Cuidado ao migrar: **não** edite a linha ativa trocando `Require` por
> `VerifyFull`. Use a linha `VerifyFull` já pronta, comentada logo acima dela no
> `.env.example`. O motivo está explicado lá — um
> `Trust Server Certificate=true` sobrevivendo à edição desligaria em silêncio
> exatamente a validação que o `VerifyFull` deveria ligar, e a conexão
> continuaria funcionando, sem nada denunciar o engano.

Dois passos, nenhum feito:

1. Baixar a CA desta instância (a instância usa
   `GOOGLE_MANAGED_INTERNAL_CA`, isto é, uma CA própria — o certificado sai do
   `describe`):

   ```bash
   gcloud sql instances describe banco-1 \
     --project=sd-26-2 \
     --format="value(serverCaCert.cert)" > banco-1-ca.pem
   ```

   (ou pelo Console: **SQL → `banco-1` → Connections → Security → "Download the
   server CA certificate"**.)

2. **Mudar o compose de produção** para montar esse arquivo nos containers
   `identity` e `tasks` — hoje `deploy/docker-compose.prod.yml` só declara os
   dois secrets JWT (`jwt_private`, `jwt_public`). Seria preciso:
   - copiar o `.pem` para a VM (ex.: `/etc/todolist/cloudsql/ca.pem`, `0444` —
     um certificado de CA não é segredo, então não precisa acertar dono como as
     chaves JWT precisam);
   - acrescentar um terceiro `secrets:` (`cloudsql_ca`) e montá-lo em `identity`
     e `tasks` (hoje só `identity`/`gateway` recebem secrets — o Tasks passaria
     a receber um pela primeira vez, mudança de forma, não de conteúdo);
   - trocar, no `.env` real, as duas connection strings pela linha
     `SSL Mode=VerifyFull;Root Certificate=/run/secrets/cloudsql_ca` que o
     `.env.example` já traz pronta.

   Trabalho futuro, registrado para não se perder. **Não é bloqueio do 22/10**:
   com `ENCRYPTED_ONLY`, o canal já é cifrado com `Require`.

## 6. Testar a conexão pela VM antes de subir a stack inteira

Da própria `maquina-1-psd` (SSH), sem Docker envolvido, só para confirmar que
rede e credencial funcionam antes de colocar o compose na jogada:

```bash
sudo apt-get install -y postgresql-client

psql "host=10.30.240.3 port=5432 dbname=todolist user=todolist sslmode=require" \
  -c "SELECT version();"
```

Esperado: a linha de versão dizendo **PostgreSQL 18**.

Diagnóstico, na ordem de probabilidade agora que o peering está confirmado:

- **erro de autenticação** → senha ou usuário (passo 2);
- **`database "todolist" does not exist`** → o passo 2 não criou o banco;
- **erro de SSL / conexão recusada sem TLS** → `sslmode` ausente; a instância é
  `ENCRYPTED_ONLY` e não aceita conexão em claro;
- **travou sem erro** → a instância ainda está subindo (passo 1 leva minutos) ou
  voltou para `NEVER`. O peering, suspeito número um do runbook antigo, já está
  verificado — não comece a investigação por ele.

Cloud SQL com IP privado **não** passa pelas regras de firewall da VPC: a rota
vem do peering. A regra `todolist-allow-postgres` (tcp:5432 da VM para a tag
`todolist-db`) existe por causa do Postgres em VM do T1 e **não tem efeito
nenhum** sobre o Cloud SQL.

## 7. Custo: pare depois do ensaio — não apague

O tier da `banco-1` é `db-custom-2-8192` (2 vCPU, 8 GB), muito acima do que uma
demonstração de dez minutos precisa, e é o item mais caro do ambiente enquanto
estiver ligada. Parada, cobra essencialmente o disco (10 GB).

```bash
# depois de cada ensaio e depois da apresentação: parar.
gcloud sql instances patch banco-1 --project=sd-26-2 --activation-policy=NEVER
```

**Não apague a `banco-1`.** O runbook original recomendava `gcloud sql instances
delete` como defesa contra "esquecer a fatura ligada"; isso valia para uma
instância criada por este trabalho e descartável. A `banco-1` é anterior a esta
onda e pode ter dados de outra atividade — apagar é irreversível e não é nossa
decisão de tomar. Parar resolve o custo.

Duas ressalvas conhecidas de Cloud SQL parado: ele continua cobrando
armazenamento, e o Google pode reiniciar automaticamente uma instância parada
por muito tempo. Para o intervalo entre hoje e 03/12 isso é irrelevante; só não
conte com "parada para sempre e custo zero".

Quando o T3 fechar, a `maquina-2-psd` (o Postgres em VM do T1) pode ser
desligada de vez — o T3 **exige** que nenhuma VM de aplicação siga de pé, e a
`banco-1` já é o banco gerenciado que esse requisito pede.

## 8. Referência: criar uma instância nova (não é o caminho de hoje)

Guardado só para o caso de a `banco-1` não servir. Com o peering da VPC
`default` já existente (seção "Estado real"), o passo de PSA do runbook antigo
não se repete:

```bash
gcloud sql instances create todolist-cloudsql \
  --project=sd-26-2 \
  --database-version=POSTGRES_17 \
  --region=us-central1 \
  --tier=db-f1-micro \
  --storage-size=10 \
  --storage-type=SSD \
  --network=default \
  --no-assign-ip
```

`db-f1-micro` (1 vCPU compartilhada, 614,4 MiB) **existe** neste projeto em
`us-central1` — verificado em 25/09/2026 com `gcloud sql tiers list`. O custo em
uso contínuo fica na ordem de **US$ 7–10/mês** segundo a documentação de preços
e comparativos de 2026, não uma fatura real; confirme na calculadora do Console.
`POSTGRES_17` aqui casaria com as imagens `postgres:17-alpine` do compose — o
oposto do que a seção 4 descreve para a `banco-1`.

## O que este runbook NÃO verificou

- **Nenhum comando de escrita foi executado.** O estado da seção "Estado real"
  vem só de leitura (`gcloud sql instances describe/list`, `gcloud compute
  networks/addresses/firewall-rules list`, `gcloud compute instances describe`,
  `gcloud services list`), em 30/09/2026. Ligar a instância, criar banco/usuário
  e conectar a stack continuam por fazer.
- **Não se sabe se `banco-1` já tem o banco `todolist` e o usuário
  `todolist`** — a listagem exige a instância no ar (seção 2).
- **Não se sabe o que mais usa a `banco-1`.** Ela é anterior a este trabalho;
  este runbook assume que conviver é possível (um banco novo ao lado do que
  houver), e por isso lista antes de criar e não apaga nada.
- `psql` 17 (imagem do `migrate`) contra servidor 18 **não foi exercitado**
  contra esta instância — ver seção 4.
- A migração para `SSL Mode=VerifyFull` (seção 5) está descrita, não
  implementada: o `docker-compose.prod.yml` real ainda não tem o secret
  `cloudsql_ca`.
