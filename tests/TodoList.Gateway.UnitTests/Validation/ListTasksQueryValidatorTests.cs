using FluentAssertions;
using TodoList.Gateway.Api.Configuration;
using TodoList.Gateway.Api.Validation;
using Xunit;

namespace TodoList.Gateway.UnitTests.Validation;

/// <summary>BE-41, CA-19 — cobertura de unidade do validador de borda de <c>GET /api/tasks</c>.</summary>
public class ListTasksQueryValidatorTests
{
    private readonly PagingOptions _options = new() { DefaultPageSize = 20, MaxPageSize = 100 };

    [Fact]
    public void Validate_PageEPageSizeAusentes_Valido()
    {
        var errors = ListTasksQueryValidator.Validate(null, null, _options);

        errors.Should().BeNull();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    public void Validate_PageValida_Valido(int page)
    {
        var errors = ListTasksQueryValidator.Validate(page, null, _options);

        errors.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_PageForaDaFaixa_Invalido(int page) // CA-19
    {
        var errors = ListTasksQueryValidator.Validate(page, null, _options);

        errors.Should().NotBeNull();
        errors!.Should().ContainKey(ListTasksQueryValidator.PageFieldName);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public void Validate_PageSizeNoLimite_Valido(int pageSize)
    {
        var errors = ListTasksQueryValidator.Validate(null, pageSize, _options);

        errors.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    [InlineData(-5)]
    public void Validate_PageSizeForaDaFaixa_Invalido(int pageSize) // CA-19
    {
        var errors = ListTasksQueryValidator.Validate(null, pageSize, _options);

        errors.Should().NotBeNull();
        errors!.Should().ContainKey(ListTasksQueryValidator.PageSizeFieldName);
    }

    [Fact]
    public void Validate_PageEPageSizeForaDaFaixa_DevolveOsDoisCampos()
    {
        var errors = ListTasksQueryValidator.Validate(0, 999, _options);

        errors.Should().NotBeNull();
        errors!.Should().ContainKey(ListTasksQueryValidator.PageFieldName);
        errors.Should().ContainKey(ListTasksQueryValidator.PageSizeFieldName);
    }

    [Fact]
    public void Validate_MaxPageSizeConfigurado_RespeitaOValorDaConfiguracao()
    {
        var options = new PagingOptions { DefaultPageSize = 20, MaxPageSize = 50 };

        ListTasksQueryValidator.Validate(null, 50, options).Should().BeNull();
        ListTasksQueryValidator.Validate(null, 51, options).Should().NotBeNull();
    }
}
