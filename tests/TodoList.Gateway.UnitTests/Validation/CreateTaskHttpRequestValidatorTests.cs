using FluentAssertions;
using TodoList.Gateway.Api.Contracts;
using TodoList.Gateway.Api.Validation;
using Xunit;

namespace TodoList.Gateway.UnitTests.Validation;

/// <summary>BE-36, CA-05/CA-06/CA-08 — cobertura de unidade do validador de borda de <c>POST /api/tasks</c>.</summary>
public class CreateTaskHttpRequestValidatorTests
{
    private readonly CreateTaskHttpRequestValidator _validator = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_TituloAusenteOuEmBranco_Invalido(string? title) // CA-05
    {
        var result = _validator.Validate(new CreateTaskHttpRequest(title, null, null, null));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateTaskHttpRequest.Title));
    }

    [Fact]
    public void Validate_TituloMaiorQue200Caracteres_Invalido()
    {
        var title = new string('a', CreateTaskHttpRequestValidator.TitleMaxLength + 1);

        var result = _validator.Validate(new CreateTaskHttpRequest(title, null, null, null));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_TituloValidoApenasComEspacosNasBordas_Valido()
    {
        var result = _validator.Validate(new CreateTaskHttpRequest("  Comprar leite  ", null, null, null));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_DescricaoMaiorQue2000Caracteres_Invalido()
    {
        var description = new string('a', CreateTaskHttpRequestValidator.DescriptionMaxLength + 1);

        var result = _validator.Validate(new CreateTaskHttpRequest("Título", description, null, null));

        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("Low")]
    [InlineData("MEDIUM")]
    [InlineData("high")]
    public void Validate_PrioridadeValidaCaseInsensitive_Valido(string priority)
    {
        var result = _validator.Validate(new CreateTaskHttpRequest("Título", null, priority, null));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_PrioridadeForaDoEnum_Invalido() // CA-06
    {
        var result = _validator.Validate(new CreateTaskHttpRequest("Título", null, "Urgente", null));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateTaskHttpRequest.Priority));
    }

    [Fact]
    public void Validate_DueDateForaDoFormato_Invalido() // CA-08
    {
        var result = _validator.Validate(new CreateTaskHttpRequest("Título", null, null, "31/12/2026"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateTaskHttpRequest.DueDate));
    }

    [Fact]
    public void Validate_DueDateNoFormatoCorreto_Valido()
    {
        var result = _validator.Validate(new CreateTaskHttpRequest("Título", null, null, "2026-12-31"));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_RequestCompletoValido_Valido()
    {
        var result = _validator.Validate(new CreateTaskHttpRequest("Título", "Descrição", "High", "2026-12-31"));

        result.IsValid.Should().BeTrue();
    }
}
