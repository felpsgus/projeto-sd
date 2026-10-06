using FluentAssertions;
using TodoList.Gateway.Api.Contracts;
using TodoList.Gateway.Api.Validation;
using Xunit;

namespace TodoList.Gateway.UnitTests.Validation;

/// <summary>BE-19 — cobertura de unidade do validador de borda de <c>PUT /api/tasks/{id}</c>, espelho de <c>CreateTaskHttpRequestValidatorTests</c>.</summary>
public class UpdateTaskHttpRequestValidatorTests
{
    private readonly UpdateTaskHttpRequestValidator _validator = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_TituloAusenteOuEmBranco_Invalido(string? title)
    {
        var result = _validator.Validate(new UpdateTaskHttpRequest(title, null, null, null));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(UpdateTaskHttpRequest.Title));
    }

    [Fact]
    public void Validate_TituloMaiorQue200Caracteres_Invalido()
    {
        var title = new string('a', UpdateTaskHttpRequestValidator.TitleMaxLength + 1);

        var result = _validator.Validate(new UpdateTaskHttpRequest(title, null, null, null));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_TituloValidoApenasComEspacosNasBordas_Valido()
    {
        var result = _validator.Validate(new UpdateTaskHttpRequest("  Comprar leite  ", null, null, null));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_DescricaoMaiorQue2000Caracteres_Invalido()
    {
        var description = new string('a', UpdateTaskHttpRequestValidator.DescriptionMaxLength + 1);

        var result = _validator.Validate(new UpdateTaskHttpRequest("Título", description, null, null));

        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("Low")]
    [InlineData("MEDIUM")]
    [InlineData("high")]
    public void Validate_PrioridadeValidaCaseInsensitive_Valido(string priority)
    {
        var result = _validator.Validate(new UpdateTaskHttpRequest("Título", null, priority, null));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_PrioridadeForaDoEnum_Invalido()
    {
        var result = _validator.Validate(new UpdateTaskHttpRequest("Título", null, "Urgente", null));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(UpdateTaskHttpRequest.Priority));
    }

    [Fact]
    public void Validate_DueDateForaDoFormato_Invalido()
    {
        var result = _validator.Validate(new UpdateTaskHttpRequest("Título", null, null, "31/12/2026"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(UpdateTaskHttpRequest.DueDate));
    }

    [Fact]
    public void Validate_DueDateNoFormatoCorreto_Valido()
    {
        var result = _validator.Validate(new UpdateTaskHttpRequest("Título", null, null, "2026-12-31"));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_RequestCompletoValido_Valido()
    {
        var result = _validator.Validate(new UpdateTaskHttpRequest("Título", "Descrição", "High", "2026-12-31"));

        result.IsValid.Should().BeTrue();
    }

    [Fact] // BE-19, nota técnica: description/dueDate ausentes (null) são o formato de "limpar" o campo, não um erro
    public void Validate_DescricaoEDueDateNulos_Valido()
    {
        var result = _validator.Validate(new UpdateTaskHttpRequest("Título", null, null, null));

        result.IsValid.Should().BeTrue();
    }
}
