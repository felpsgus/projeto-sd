# Cloud SQL for PostgreSQL — runbook de provisionamento (Onda C, metade 2)

Runbook para colocar o Postgres do roteiro de 22/10 fora da VM, no Cloud SQL for
PostgreSQL (projeto `sd-26-2`, `us-central1`), na mesma VPC de `maquina-1-psd`
(`10.128.0.4`, ver `deploy/README.md`).

> **Este runbook NUNCA foi executado.** Nenhum comando `gcloud` de escrita
> (`create`, `patch`, peering, firewall) foi rodado — o agente que escreveu isto
> está proibido de criar recursos que geram custo sem autorização explícita do
> usuário (ver `deploy/todolist.env.example` e `deploy/docker-compose.prod.yml`
> para o que a Onda C efetivamente validou: o compose de produção contra um
> Postgres LOCAL com TLS, não contra este Cloud SQL). Trate cada comando abaixo
> como PROPOSTA revisada, não como fato testado, e rode o primeiro ensaio com
> folga — mesmo espírito do aviso no topo de `deploy/README.md` para o T2.

## 0. Pré-requisitos que faltam antes do primeiro comando

- `gcloud` autenticado no projeto certo: `gcloud config set project sd-26-2`.
- Saber o nome da VPC onde `maquina-1-psd` vive. Se o projeto nunca criou uma
  VPC própria, é a rede automática `default`. Confirme antes de continuar —
  errar a rede aqui faz a instância nascer numa VPC que a VM não enxerga, e o
  sintoma só aparece depois, como timeout de conexão, não como erro claro:

  ```
  gcloud compute instances describe maquina-1-psd --zone=us-central1-a \
    --format="value(networkInterfaces[0].network)"
  ```

## 1. Acesso privado ao serviço (Private Services Access) — o passo que todo mundo esquece

Uma instância do Cloud SQL com IP privado não fica "dentro" da sua VPC: ela
mora numa VPC gerenciada pelo Google, e as duas se enxergam por um VPC
**peering** chamado *Private Services Access*. Sem isso configurado
**primeiro**, o `gcloud sql instances create --network=... --no-assign-ip`
mais abaixo falha (ou, pior, fica pendurado) reclamando que não existe uma
alocação de IP para o serviço `servicenetworking.googleapis.com` nessa rede.
Isso só precisa ser feito **uma vez por VPC**, não uma vez por instância.

```bash
# 1a. Reserva um bloco de IPs (não uma instância, só o intervalo) para o
#     Google usar nesse peering. /24 é o menor bloco que o Cloud SQL aceita
#     por região — deixe folga (aqui, /24 já é o próprio tamanho mínimo).
gcloud compute addresses create google-managed-services-default \
  --global \
  --purpose=VPC_PEERING \
  --prefix-length=24 \
  --network=default \
  --project=sd-26-2

# 1b. Cria o peering em si, usando o bloco reservado acima.
gcloud services vpc-peerings connect \
  --service=servicenetworking.googleapis.com \
  --ranges=google-managed-services-default \
  --network=default \
  --project=sd-26-2
```

Troque `default` pelo nome real da VPC confirmado no passo 0, nos **três**
lugares onde aparece acima (nome do endereço reservado é só um rótulo seu,
mas `--network` tem que ser o de verdade nos dois comandos).

## 2. Criar a instância

**Versão: PostgreSQL 17** — a mesma major do `postgres:17-alpine` que a Onda C
já validou localmente (`deploy/docker-compose.prod.yml` + o override de teste
usado nesta onda). Divergir de major entre dev/teste e produção é comprar um
problema de compatibilidade de tipo/extensão que só aparece na hora da
apresentação.

**Tamanho:** `db-f1-micro` (shared-core, 1 vCPU compartilhada, ~0,6 GB de RAM)
é o menor tier que o Cloud SQL for PostgreSQL oferece e é suficiente para uma
demonstração com uma dúzia de requisições — não é elegível a desconto por
compromisso nem tem o SLA padrão do Cloud SQL, o que é irrelevante aqui. Ordem
de grandeza do custo: **em torno de US$ 7–10/mês rodando 24 horas por dia**
(cobrança por segundo — poucas horas de uso no dia da apresentação custam
centavos), mais uma fração pequena de armazenamento (10 GB SSD já é mais que
suficiente). Não tomei este número de uma fatura real — é o que a
documentação de preços do Cloud SQL e comparativos de terceiros indicam para
`us-central1` em 2026; confirme na calculadora de preços do Console antes de
criar, porque preço de nuvem muda sem aviso.

**IP privado, sem IP público** (`--no-assign-ip`): é o que o briefing pede e o
que faz sentido com a VM já estando na mesma VPC — nenhum motivo para expor a
instância à internet.

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

Isso demora alguns minutos (o Cloud SQL provisiona uma VM gerenciada por
trás). Espere o comando retornar antes de seguir — ele bloqueia até a
instância existir ou falhar.

## 3. Banco e usuário de aplicação

A instância acima não vem com o banco `todolist` nem o usuário `todolist` —
só a instância em si (que já tem um usuário `postgres` administrativo,
inutilizado pela aplicação).

```bash
gcloud sql databases create todolist --instance=todolist-cloudsql --project=sd-26-2

gcloud sql users create todolist \
  --instance=todolist-cloudsql \
  --password=TROQUE_ESTA_SENHA \
  --project=sd-26-2
```

A senha aqui é a mesma que entra em `PGPASSWORD`/`ConnectionStrings__*Db` do
`.env` real na VM (`deploy/todolist.env.example`) — nunca a mesma senha do
`.env` de teste local que a Onda C usou e já apagou.

## 4. Onde ler o IP privado

Console: **SQL → `todolist-cloudsql` → Overview → "Private IP address"**.

Por linha de comando:

```bash
gcloud sql instances describe todolist-cloudsql \
  --project=sd-26-2 \
  --format="value(ipAddresses[?type=PRIVATE].ipAddress)"
```

Esse valor é o que substitui `<IP_PRIVADO_CLOUDSQL>` nas duas connection
strings e nas variáveis `PGHOST`/etc. de `deploy/todolist.env.example`, no
`.env` real da VM (nunca commitado).

## 5. Baixar o certificado da CA — o que falta para `VerifyFull`

`deploy/todolist.env.example` documenta duas formas de TLS: a pragmática usada
hoje (`SSL Mode=Require` — cifra o canal, não valida a CA nem protege contra
man-in-the-middle) e o alvo (`SSL Mode=VerifyFull` — valida a cadeia
completa). A Onda C testou contra um Postgres local com certificado
autoassinado e confirmou que `Require` conecta.

> Cuidado ao migrar: **não** edite a linha ativa trocando a palavra `Require`
> por `VerifyFull`. Use a linha `VerifyFull` já pronta, comentada logo acima
> dela no `.env.example`. O motivo está explicado lá — um
> `Trust Server Certificate=true` sobrevivendo à edição desligaria em silêncio
> exatamente a validação que o `VerifyFull` deveria ligar, e a conexão
> continuaria funcionando, sem nada denunciar o engano.

Migrar para `VerifyFull` contra o Cloud SQL de verdade exige dois passos que
não foram feitos ainda:

1. Baixar o certificado da CA do servidor desta instância:

   ```bash
   gcloud sql instances describe todolist-cloudsql \
     --project=sd-26-2 \
     --format="value(serverCaCert.cert)" > todolist-cloudsql-ca.pem
   ```

   (ou pelo Console: **SQL → instância → Connections → Security → "Download
   the server CA certificate"**.)

2. **Mudar o compose de produção** para montar esse arquivo dentro dos
   containers `identity` e `tasks` — hoje `deploy/docker-compose.prod.yml` só
   declara os dois secrets JWT (`jwt_private`, `jwt_public`). Seria preciso:
   - copiar `todolist-cloudsql-ca.pem` para a VM (ex.:
     `/etc/todolist/cloudsql/ca.pem`, dono/permissão de leitura para o uid do
     container — mesma lógica do `chown 1654` documentada para as chaves JWT
     em `docker-compose.prod.yml`, mas mais simples: um certificado de CA não
     é segredo, então `0444` já basta, sem precisar acertar dono);
   - acrescentar um terceiro `secrets:` (`cloudsql_ca`) e montá-lo em
     `identity` e `tasks` (hoje só `identity`/`gateway` recebem secrets — o
     Tasks passaria a receber um secret pela primeira vez, o que é uma mudança
     de forma, não só de conteúdo, e merece revisão);
   - trocar, no `.env` real, `SSL Mode=Require` por
     `SSL Mode=VerifyFull;Root Certificate=/run/secrets/cloudsql_ca` nas duas
     connection strings, usando a linha comentada que
     `deploy/todolist.env.example` já traz pronta para isso.

   Nada disso foi feito nesta onda — é trabalho futuro, registrado aqui para
   não se perder.

## 6. Testar a conexão pela VM antes de subir a stack inteira

Da própria `maquina-1-psd` (SSH), sem Docker envolvido, só para confirmar que
rede + firewall + credencial funcionam antes de colocar o compose inteiro na
jogada:

```bash
# instala psql se ainda não tiver (mesma VM do systemd/plano B)
sudo apt-get install -y postgresql-client

psql "host=<IP_PRIVADO_CLOUDSQL> port=5432 dbname=todolist user=todolist sslmode=require" \
  -c "SELECT version();"
```

Se isso travar (nem conecta nem dá erro rápido), o suspeito número um é o
peering do passo 1 não ter sido criado antes da instância, ou a instância ter
sido criada numa VPC diferente da VM — não é caso de firewall de porta (Cloud
SQL com IP privado não passa pelas regras de firewall da VPC do jeito que uma
segunda VM passaria; o próprio peering já implica a rota). Se der erro de
autenticação, é senha ou usuário; se dor "database does not exist", o passo 3
não rodou.

## 7. A instância custa dinheiro enquanto existir — e como parar isso

Diferente de uma VM que se pode desligar de graça, um Cloud SQL PARADO
(`STOPPED`) ainda cobra armazenamento (pouco, mas não zero) e o Cloud SQL só
aceita ficar parado por um número limitado de dias antes de reiniciar
sozinho. Para uma demonstração de um dia só, a opção mais simples e mais
segura contra "esquecer e a fatura continuar" é **apagar a instância** depois
da apresentação, não só parar:

```bash
# opção 1 (recomendada pós-demo): apaga de vez.
gcloud sql instances delete todolist-cloudsql --project=sd-26-2

# opção 2 (se for reusar em poucos dias): só para, sem apagar.
gcloud sql instances patch todolist-cloudsql --project=sd-26-2 --activation-policy=NEVER
```

Depois de apagar a instância, o peering do passo 1 (`google-managed-services-*`)
pode continuar existindo sem custo — ele é só uma reserva de IP e uma
conexão de rede, reutilizável se um novo Cloud SQL nascer nessa VPC no
futuro; não precisa (nem deveria, sem necessidade) ser desfeito.

## O que este runbook NÃO verificou

- Nenhum comando `gcloud sql`/`gcloud compute addresses`/`gcloud services
  vpc-peerings` acima foi executado — só validados por leitura da
  documentação oficial do Cloud SQL e comparativos de preço de 2026 (ver
  fontes usadas na pesquisa desta onda: a página oficial
  `cloud.google.com/sql/pricing`, `cloud.google.com/sql/docs/postgres/
  configure-private-services-access` e `cloud.google.com/sql/docs/postgres/
  create-instance`).
- O valor de `--network` ficou como `default` porque não há evidência no
  repositório de que o projeto usa outra VPC — confirme com o comando do
  passo 0 antes de rodar qualquer coisa.
- O preço de `db-f1-micro` é uma ordem de grandeza (~US$ 7–10/mês em uso
  contínuo), não uma cotação exata — confirme na calculadora de preços do
  Console antes de criar, e lembre que storage/backup são cobrados à parte.
  A **existência** do tier, essa sim, foi verificada em 25/09/2026 com
  `gcloud sql tiers list --project=sd-26-2`: `db-f1-micro` (614,4 MiB de RAM)
  aparece disponível em `us-central1` para este projeto. Só o preço é
  estimativa; o nome do tier não é.
- A migração para `SSL Mode=VerifyFull` (seção 5) está descrita, não
  implementada: o `docker-compose.prod.yml` real ainda não tem o terceiro
  secret `cloudsql_ca`.
