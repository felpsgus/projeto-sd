namespace TodoList.Gateway.Api.Endpoints;

/// <summary>
/// <c>Cache-Control: no-store</c> nas respostas que carregam ou apagam
/// credenciais (login, refresh, logout — BE-09 CA-13, BE-10 CA-21). Definido
/// <b>antes</b> do handler, para valer também nas respostas de erro.
/// </summary>
public static class NoStoreEndpointFilter
{
    public static RouteHandlerBuilder WithNoStore(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";

            return await next(context);
        });
}
