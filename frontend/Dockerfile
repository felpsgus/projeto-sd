# Etapa 13 (Onda A). Contexto de build = esta pasta (frontend/), DIFERENTE dos
# três Dockerfiles .NET (src/*/TodoList.*.Api/Dockerfile), que usam a raiz do
# repositório porque o restore precisa enxergar contracts/, global.json e
# Directory.Build.props. O frontend Angular não depende de nada fora de
# frontend/ — build normal, a partir desta pasta:
#
#   docker build -t todolist-frontend ./frontend
#
# Node 24.21.0 tem que bater exatamente com frontend/.nvmrc (o projeto exige
# Node 22/24 LTS).
#
# ATENÇÃO: o build do Angular (application builder, Angular 17+, sem
# outputPath declarado em angular.json) sai em dist/frontend/browser, NÃO em
# dist/frontend — essa pasta contém uma subpasta browser/ e nenhum index.html
# na raiz. Copiar o caminho errado faz o nginx responder 403/404 em tudo.
#
# Este arquivo e deploy/nginx/todolist.conf/frontend/nginx.conf.template são
# gêmeos (mesmas regras, dois lugares — container e VM via systemd/nginx no
# host). Ver o comentário no topo de cada um: uma mudança de regra precisa ser
# replicada no outro.

FROM node:24.21.0-alpine AS build
WORKDIR /app

# --- Camada de dependências ---------------------------------------------
# Copia só o necessário para `npm ci` primeiro: uma mudança de código sem
# mudança de dependência reaproveita esta camada.
COPY package.json package-lock.json ./
RUN npm ci

# --- Build --------------------------------------------------------------
COPY . .
RUN npm run build

# --- Imagem final ---------------------------------------------------------
# nginx-unprivileged: roda como usuário não-root (nginx) por padrão — por
# isso a porta é 8080, não 80 (usuário sem privilégio não pode bindar porta
# < 1024). Não acrescente USER root em lugar nenhum.
FROM nginxinc/nginx-unprivileged:alpine AS final

# O entrypoint da imagem (/docker-entrypoint.d/20-envsubst-on-templates.sh)
# roda envsubst sobre /etc/nginx/templates/*.template e escreve o resultado em
# /etc/nginx/conf.d/. Por padrão, envsubst substitui TODA sequência $NOME que
# encontrar no arquivo — inclusive as variáveis do PRÓPRIO nginx ($uri, $host,
# $scheme, $proxy_add_x_forwarded_for), que não são passadas por -e e viram
# string vazia. O sintoma é traiçoeiro: a home carrega normal (não usa
# nenhuma dessas variáveis no caminho feliz), e só um F5 numa rota profunda
# (/tasks, /login) expõe o 404, porque try_files perdeu $uri.
#
# NGINX_ENVSUBST_FILTER restringe a substituição a um regex de nomes — aqui,
# só as duas variáveis que o template realmente usa. Quem remover ou alargar
# este filtro quebra o roteamento do SPA em silêncio (a home continua
# funcionando).
ENV NGINX_ENVSUBST_FILTER="^(GATEWAY_UPSTREAM|NGINX_LOCAL_RESOLVERS)$"

# Liga /docker-entrypoint.d/15-local-resolvers.envsh, que é OPT-IN (sem esta
# variável ele sai na primeira linha). Ele lê os nameservers do /etc/resolv.conf
# DESTE container e exporta NGINX_LOCAL_RESOLVERS, que o template usa na
# diretiva `resolver`. Como roda no passo 15, antes do envsubst do passo 20, o
# valor já está disponível para a substituição.
#
# É o que torna o `resolver` portátil: no docker-compose o resolv.conf aponta
# para o DNS interno do Docker, no Cloud Run (T3) para o resolver do ambiente
# de lá — o template não precisa saber qual é nem trazer nenhum IP fixo. Ver a
# nota sobre resolução de DNS em nginx.conf.template.
ENV NGINX_ENTRYPOINT_LOCAL_RESOLVERS=1

COPY nginx.conf.template /etc/nginx/templates/default.conf.template

COPY --from=build /app/dist/frontend/browser /usr/share/nginx/html

EXPOSE 8080
