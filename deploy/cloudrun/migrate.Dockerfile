# BE-44. Aplica os dois SQL idempotentes (Identity antes do Tasks) no Postgres do Cloud Run.
# Contexto de build = artifacts/sql/ (o .dockerignore da raiz exclui artifacts/):
#
#   pwsh scripts/new-migrations-sql.ps1
#   docker build -f deploy/cloudrun/migrate.Dockerfile -t todolist-migrate artifacts/sql
#
# A conexão vem de PGHOST/PGPORT/PGDATABASE/PGUSER/PGPASSWORD/PGSSLMODE=require.
# Só o cliente psql (não o servidor Postgres), para a imagem ficar pequena.
FROM alpine:3.23
RUN apk add --no-cache postgresql18-client
COPY 01-identity.sql 02-tasks.sql /sql/
USER nobody
ENTRYPOINT ["psql", "-v", "ON_ERROR_STOP=1", "-f", "/sql/01-identity.sql", "-f", "/sql/02-tasks.sql"]
