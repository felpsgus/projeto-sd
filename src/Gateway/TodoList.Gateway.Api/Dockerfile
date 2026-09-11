# BE-38. Contexto de build = RAIZ do repositório (não esta pasta) — o
# restore precisa enxergar contracts/, Directory.Build.props e global.json,
# que ficam fora de src/Gateway/. Construa a partir da raiz:
#
#   docker build -f src/Gateway/TodoList.Gateway.Api/Dockerfile -t todolist-gateway .
#
# Ver a nota técnica "Por que o contexto de build é a raiz" em
# tarefas/backend/BE-38-containerizacao.md.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# --- Camada de restore -------------------------------------------------
# O Gateway não referencia nenhum projeto do Identity/Tasks/SharedKernel
# (D-33) — só os dois .proto, como cliente gRPC (D-29, D-32). Uma alteração
# de código sem alteração de dependência reaproveita esta camada.
COPY global.json Directory.Build.props .editorconfig ./

COPY contracts/identity/v1/identity.proto contracts/identity/v1/identity.proto
COPY contracts/tasks/v1/tasks.proto contracts/tasks/v1/tasks.proto

COPY src/Gateway/TodoList.Gateway.Api/TodoList.Gateway.Api.csproj src/Gateway/TodoList.Gateway.Api/

RUN dotnet restore src/Gateway/TodoList.Gateway.Api/TodoList.Gateway.Api.csproj

# --- Camada de publish ---------------------------------------------------
COPY src/Gateway/TodoList.Gateway.Api/ src/Gateway/TodoList.Gateway.Api/

RUN dotnet publish src/Gateway/TodoList.Gateway.Api/TodoList.Gateway.Api.csproj \
    -c Release --no-restore -o /app

# --- Imagem final ---------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# A imagem base fixa ASPNETCORE_HTTP_PORTS=8080; aqui o endpoint vem do
# Kestrel via configuração (abaixo), não desta variável — neutralizada para
# não gerar o aviso "Overriding HTTP_PORTS".
ENV ASPNETCORE_HTTP_PORTS=
ENV ASPNETCORE_ENVIRONMENT=Production

# D-32/D-37: única origem pública, HTTP/1 na mesma porta que vira $PORT no
# Cloud Run.
ENV Kestrel__Endpoints__Http__Url=http://+:8080

EXPOSE 8080

COPY --from=build /app .

# Usuário não-root já presente na imagem aspnet:10.0.
USER $APP_UID

ENTRYPOINT ["dotnet", "TodoList.Gateway.Api.dll"]
