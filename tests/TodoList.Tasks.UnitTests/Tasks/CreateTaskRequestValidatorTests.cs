using FluentAssertions;
using TodoList.Tasks.Api.Validation;
using TodoList.Tasks.Application.Tasks;
using Xunit;

namespace TodoList.Tasks.UnitTests.Tasks;

/// <summary>
/// <see cref="CreateTaskRequestValidator"/> (BE-17) — CA-08, CA-09, CA-10.
/// CA-05 (priority inválida) e CA-13 (dueDate malformado) não são regras
/// deste validador: os dois já falham na desserialização do corpo JSON,
/// antes de qualquer <c>IValidator&lt;T&gt;</c> rodar — cobertos por teste de
/// integração (<c>CreateTaskEndpointTests</c>).
/// </summary>
public class CreateTaskRequestValidatorTests
{
    private readonly CreateTaskRequestValidator _validator = new();

    [Theory] // CA-08
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_TituloVazioOuComEspacos_EInvalido(string title)
    {
        var result = _validator.Validate(new CreateTaskRequest(title, null, null, null));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == nameof(CreateTaskRequest.Title));
    }

    [Fact] // CA-08 — 201 caracteres
    public void Validate_TituloCom201Caracteres_EInvalido()
    {
        var result = _validator.Validate(new CreateTaskRequest(new string('a', 201), null, null, null));

        result.IsValid.Should().BeFalse();
    }

    [Theory] // CA-09 — bordas 1 e 200
    [InlineData(1)]
    [InlineData(200)]
    public void Validate_TituloNasBordas_EValido(int length)
    {
        var result = _validator.Validate(new CreateTaskRequest(new string('a', length), null, null, null));

        result.IsValid.Should().BeTrue();
    }

    [Fact] // CA-10 — 2000 é aceita
    public void Validate_DescricaoCom2000Caracteres_EValida()
    {
        var result = _validator.Validate(new CreateTaskRequest("Título válido", new string('d', 2000), null, null));

        result.IsValid.Should().BeTrue();
    }

    [Fact] // CA-10 — 2001 é rejeitada
    public void Validate_DescricaoCom2001Caracteres_EInvalida()
    {
        var result = _validator.Validate(new CreateTaskRequest("Título válido", new string('d', 2001), null, null));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == nameof(CreateTaskRequest.Description));
    }

    [Fact]
    public void Validate_DescricaoNula_EValida()
    {
        var result = _validator.Validate(new CreateTaskRequest("Título válido", null, null, null));

        result.IsValid.Should().BeTrue();
    }
}
