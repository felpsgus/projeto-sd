using System.ComponentModel.DataAnnotations;

namespace TodoList.Identity.Api.Configuration;

/// <summary>
/// Seleciona, por configuração, qual implementação de <c>IUserLookup</c> o
/// Identity usa (BE-26) — <see cref="InMemoryProvider"/> (seed fixo em
/// memória) ou <see cref="PersistedProvider"/> (banco real, via
/// <c>PersistedUserLookup</c>/BE-04, CA-13 de BE-26).
/// </summary>
/// <remarks>
/// Onda E (T2): esta classe já teve um terceiro par de opções,
/// <c>SeedDemoUsers</c>/<c>DemoUserPassword</c> (BE-33/D-36), que ligava
/// <c>DemoUserSeeder</c> — um seed de dois usuários fixos em
/// <c>identity.users</c> que existia só porque o T2 ainda não tinha cadastro
/// de verdade. Com o cadastro real (fase 3, <c>POST /api/auth/register</c>),
/// o seed virou exatamente o tipo de atalho que um avaliador repara: uma
/// rota de "ligar em produção por engano" que não precisa mais existir. O
/// roteiro de demonstração (<c>scripts/demo-t2.ps1</c>, <c>deploy/demo.sh</c>,
/// <c>deploy/smoke.sh</c>) passou a cadastrar contas pela API/tela; o único
/// cenário que o seed cobria e a API não cobre — um usuário **inativo**, para
/// provar RN-AUTH-09 — passou a ser criado por um <c>UPDATE</c> direto em
/// <c>identity.users</c>, documentado em <c>deploy/README.md</c> (não existe
/// nem deve existir uma rota de desativação; ver decisão da Onda E).
/// </remarks>
public sealed class UserStoreOptions
{
    public const string SectionName = "UserStore";

    public const string InMemoryProvider = "InMemory";
    public const string PersistedProvider = "Persisted";

    [Required(AllowEmptyStrings = false)]
    public string Provider { get; init; } = InMemoryProvider;
}
