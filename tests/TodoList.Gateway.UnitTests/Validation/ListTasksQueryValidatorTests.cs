using FluentAssertions;
using TodoList.Gateway.Api.Configuration;
using TodoList.Gateway.Api.Validation;
using Xunit;

namespace TodoList.Gateway.UnitTests.Validation;

/// <summary>BE-41 CA-19; BE-22 CA-05 a CA-17 — cobertura de unidade do validador de borda de <c>GET /api/tasks</c>.</summary>
public class ListTasksQueryValidatorTests
{
    private readonly PagingOptions _options = new() { DefaultPageSize = 20, MaxPageSize = 100 };

    private ListTasksQueryValidationResult Validate(
        int? page = null,
        int? pageSize = null,
        string? status = null,
        IReadOnlyList<string>? priority = null,
        string? overdue = null,
        string? search = null) =>
        ListTasksQueryValidator.Validate(page, pageSize, status, priority, overdue, search, _options);

    [Fact]
    public void Validate_TudoAusente_ValidoComPadroes()
    {
        var result = Validate();

        result.IsValid.Should().BeTrue();
        result.Request!.Page.Should().Be(1);
        result.Request.PageSize.Should().Be(20);
        result.Request.Status.Should().Be("all");
        result.Request.Priority.Should().BeEmpty();
        result.Request.Overdue.Should().BeNull();
        result.Request.Search.Should().BeNull();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    public void Validate_PageValida_Valido(int page)
    {
        var result = Validate(page: page);

        result.IsValid.Should().BeTrue();
        result.Request!.Page.Should().Be(page);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_PageForaDaFaixa_Invalido(int page) // CA-19
    {
        var result = Validate(page: page);

        result.IsValid.Should().BeFalse();
        result.Errors!.Should().ContainKey(ListTasksQueryValidator.PageFieldName);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public void Validate_PageSizeNoLimite_Valido(int pageSize)
    {
        var result = Validate(pageSize: pageSize);

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    [InlineData(-5)]
    public void Validate_PageSizeForaDaFaixa_Invalido(int pageSize) // CA-19
    {
        var result = Validate(pageSize: pageSize);

        result.IsValid.Should().BeFalse();
        result.Errors!.Should().ContainKey(ListTasksQueryValidator.PageSizeFieldName);
    }

    [Fact]
    public void Validate_PageEPageSizeForaDaFaixa_DevolveOsDoisCampos()
    {
        var result = Validate(page: 0, pageSize: 999);

        result.IsValid.Should().BeFalse();
        result.Errors!.Should().ContainKey(ListTasksQueryValidator.PageFieldName);
        result.Errors.Should().ContainKey(ListTasksQueryValidator.PageSizeFieldName);
    }

    [Fact]
    public void Validate_MaxPageSizeConfigurado_RespeitaOValorDaConfiguracao()
    {
        var options = new PagingOptions { DefaultPageSize = 20, MaxPageSize = 50 };

        ListTasksQueryValidator.Validate(null, 50, null, null, null, null, options).IsValid.Should().BeTrue();
        ListTasksQueryValidator.Validate(null, 51, null, null, null, null, options).IsValid.Should().BeFalse();
    }

    [Theory] // BE-22, RN-LIST-02, CA-05
    [InlineData("pending")]
    [InlineData("completed")]
    [InlineData("all")]
    [InlineData("PENDING")]
    public void Validate_StatusValido_ResolveOValorNormalizado(string status)
    {
        var result = Validate(status: status);

        result.IsValid.Should().BeTrue();
        result.Request!.Status.Should().Be(status.Trim().ToLowerInvariant());
    }

    [Fact] // BE-22, CA-11
    public void Validate_StatusInvalido_Retorna400ComCampoStatus()
    {
        var result = Validate(status: "arquivada");

        result.IsValid.Should().BeFalse();
        result.Errors!.Should().ContainKey(ListTasksQueryValidator.StatusFieldName);
    }

    [Fact] // BE-22, RN-LIST-02 — ausência equivale a "all"
    public void Validate_StatusAusente_ResolveAll()
    {
        var result = Validate();

        result.IsValid.Should().BeTrue();
        result.Request!.Status.Should().Be("all");
    }

    [Theory] // BE-22, RN-LIST-03, CA-06
    [InlineData("low")]
    [InlineData("medium")]
    [InlineData("high")]
    [InlineData("HIGH")]
    public void Validate_PrioridadeUnicaValida_Resolve(string priority)
    {
        var result = Validate(priority: [priority]);

        result.IsValid.Should().BeTrue();
        result.Request!.Priority.Should().ContainSingle().Which.Should().Be(priority.Trim().ToLowerInvariant());
    }

    [Fact] // BE-22, CA-07 — repetição de prioridade
    public void Validate_PrioridadesRepetidas_ResolveAsDuas()
    {
        var result = Validate(priority: ["low", "high"]);

        result.IsValid.Should().BeTrue();
        result.Request!.Priority.Should().BeEquivalentTo(["low", "high"], options => options.WithStrictOrdering());
    }

    [Fact] // BE-22, CA-11
    public void Validate_PrioridadeInvalida_Retorna400ComCampoPriority()
    {
        var result = Validate(priority: ["urgente"]);

        result.IsValid.Should().BeFalse();
        result.Errors!.Should().ContainKey(ListTasksQueryValidator.PriorityFieldName);
    }

    [Fact] // BE-22, CA-11 — um elemento inválido invalida mesmo com outros válidos
    public void Validate_PrioridadeComUmElementoInvalido_Retorna400()
    {
        var result = Validate(priority: ["low", "urgente"]);

        result.IsValid.Should().BeFalse();
        result.Errors!.Should().ContainKey(ListTasksQueryValidator.PriorityFieldName);
    }

    [Fact] // BE-22, RN-LIST-03 — ausência equivale a "todas"
    public void Validate_PrioridadeAusente_ListaVazia()
    {
        var result = Validate();

        result.IsValid.Should().BeTrue();
        result.Request!.Priority.Should().BeEmpty();
    }

    [Theory] // BE-22, RN-LIST-04, CA-08/CA-09
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("True", true)]
    public void Validate_OverdueValido_ResolveOBooleano(string overdue, bool expected)
    {
        var result = Validate(overdue: overdue);

        result.IsValid.Should().BeTrue();
        result.Request!.Overdue.Should().Be(expected);
    }

    [Fact] // BE-22, RN-LIST-04 — ausência é "não filtra" (null), nunca false por padrão
    public void Validate_OverdueAusente_Null()
    {
        var result = Validate();

        result.IsValid.Should().BeTrue();
        result.Request!.Overdue.Should().BeNull();
    }

    [Fact] // BE-22, CA-11
    public void Validate_OverdueInvalido_Retorna400ComCampoOverdue()
    {
        var result = Validate(overdue: "talvez");

        result.IsValid.Should().BeFalse();
        result.Errors!.Should().ContainKey(ListTasksQueryValidator.OverdueFieldName);
    }

    [Fact] // BE-22, RN-LIST-05
    public void Validate_SearchInformado_ResolveOTextoAparado()
    {
        var result = Validate(search: "  relatório  ");

        result.IsValid.Should().BeTrue();
        result.Request!.Search.Should().Be("relatório");
    }

    [Theory] // BE-22, CA-17 — vazio ou só espaços equivale a ausente
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_SearchVazioOuEspacos_ResolveNull(string search)
    {
        var result = Validate(search: search);

        result.IsValid.Should().BeTrue();
        result.Request!.Search.Should().BeNull();
    }

    [Fact] // BE-22, CA-10 — filtros combinados na mesma chamada
    public void Validate_FiltrosCombinados_ResolveTodosOsCampos()
    {
        var result = Validate(status: "pending", priority: ["high"], overdue: "true", search: "urgente");

        result.IsValid.Should().BeTrue();
        result.Request!.Status.Should().Be("pending");
        result.Request.Priority.Should().ContainSingle().Which.Should().Be("high");
        result.Request.Overdue.Should().BeTrue();
        result.Request.Search.Should().Be("urgente");
    }

    [Fact] // BE-22, CA-11 — vários campos inválidos ao mesmo tempo devolvem todas as chaves
    public void Validate_VariosCamposInvalidos_DevolveTodasAsChaves()
    {
        var result = Validate(page: 0, status: "arquivada", priority: ["urgente"], overdue: "talvez");

        result.IsValid.Should().BeFalse();
        result.Errors!.Should().ContainKey(ListTasksQueryValidator.PageFieldName);
        result.Errors.Should().ContainKey(ListTasksQueryValidator.StatusFieldName);
        result.Errors.Should().ContainKey(ListTasksQueryValidator.PriorityFieldName);
        result.Errors.Should().ContainKey(ListTasksQueryValidator.OverdueFieldName);
    }
}
