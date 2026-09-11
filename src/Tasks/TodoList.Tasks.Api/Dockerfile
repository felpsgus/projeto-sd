# BE-38. Contexto de build = RAIZ do repositório (não esta pasta) — o
# restore precisa enxergar contracts/, Directory.Build.props e global.json,
# que ficam fora de src/Tasks/. Construa a partir da raiz:
#
#   docker build -f src/Tasks/TodoList.Tasks.Api/Dockerfile -t todolist-tasks .
#
# Ver a nota técnica "Por que o contexto de build é a raiz" em
# tarefas/backend/BE-38-containerizacao.md.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# --- Camada de restore -------------------------------------------------
# Só o que o restore do TodoList.Tasks.Api.csproj precisa: props/config da
# raiz, os .proto consumidos (D-29 — tasks.proto no lado servidor,
# identity.proto no lado cliente da Infrastructure) e os .csproj de toda a
# cadeia de ProjectReference (Api -> Infrastructure -> Application -> Domain
# -> SharedKernel). Uma alteração de código sem alteração de dependência
# reaproveita esta camada.
COPY global.json Directory.Build.props .editorconfig ./

COPY contracts/tasks/v1/tasks.proto contracts/tasks/v1/tasks.proto
COPY contracts/identity/v1/identity.proto contracts/identity/v1/identity.proto

COPY src/Tasks/TodoList.Tasks.Api/TodoList.Tasks.Api.csproj src/Tasks/TodoList.Tasks.Api/
COPY src/Tasks/TodoList.Tasks.Infrastructure/TodoList.Tasks.Infrastructure.csproj src/Tasks/TodoList.Tasks.Infrastructure/
COPY src/Tasks/TodoList.Tasks.Application/TodoList.Tasks.Application.csproj src/Tasks/TodoList.Tasks.Application/
COPY src/Tasks/TodoList.Tasks.Domain/TodoList.Tasks.Domain.csproj src/Tasks/TodoList.Tasks.Domain/
COPY src/Shared/TodoList.SharedKernel/TodoList.SharedKernel.csproj src/Shared/TodoList.SharedKernel/

RUN dotnet restore src/Tasks/TodoList.Tasks.Api/TodoList.Tasks.Api.csproj

# --- Camada de publish ---------------------------------------------------
COPY src/Tasks/TodoList.Tasks.Api/ src/Tasks/TodoList.Tasks.Api/
COPY src/Tasks/TodoList.Tasks.Infrastructure/ src/Tasks/TodoList.Tasks.Infrastructure/
COPY src/Tasks/TodoList.Tasks.Application/ src/Tasks/TodoList.Tasks.Application/
COPY src/Tasks/TodoList.Tasks.Domain/ src/Tasks/TodoList.Tasks.Domain/
COPY src/Shared/TodoList.SharedKernel/ src/Shared/TodoList.SharedKernel/

RUN dotnet publish src/Tasks/TodoList.Tasks.Api/TodoList.Tasks.Api.csproj \
    -c Release --no-restore -o /app

# --- Imagem final ---------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# A imagem base fixa ASPNETCORE_HTTP_PORTS=8080; aqui os endpoints vêm do
# Kestrel via configuração (abaixo), não desta variável — neutralizada para
# não gerar o aviso "Overriding HTTP_PORTS".
ENV ASPNETCORE_HTTP_PORTS=
ENV ASPNETCORE_ENVIRONMENT=Production

# D-37: o endpoint Grpc (Http2) é o que vira $PORT no Cloud Run — escuta em
# 8080. O endpoint Http (Http1, só /health) fica em 8081, interno ao compose,
# nunca publicado no host.
ENV Kestrel__Endpoints__Grpc__Url=http://+:8080
ENV Kestrel__Endpoints__Http__Url=http://+:8081

EXPOSE 8080
EXPOSE 8081

COPY --from=build /app .

# Usuário não-root já presente na imagem aspnet:10.0.
USER $APP_UID

ENTRYPOINT ["dotnet", "TodoList.Tasks.Api.dll"]
