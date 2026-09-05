using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using TodoList.Identity.Domain.Users;
using Xunit;

namespace TodoList.Identity.UnitTests;

/// <summary>
/// Entidade <see cref="User"/> (BE-04) — <c>Create</c> (CA-04 a CA-06),
/// <c>Rename</c> (CA-09) e os demais comportamentos de domínio.
/// </summary>
public class UserTests
{
    private static readonly Email _validEmail = Email.Create("ada@exemplo.com").Value;

    [Fact] // CA-04
    public void Create_SemDisplayName_UsaParteAntesDoArroba()
    {
        var result = User.Create(_validEmail, displayName: null, "hash", CreateTimeProvider());

        result.IsSuccess.Should().BeTrue();
        result.Value.DisplayName.Should().Be("ada");
    }

    [Theory] // CA-04 — vazio/só espaços cai no mesmo padrão de "ausente"
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ComDisplayNameVazioOuSoEspacos_UsaParteAntesDoArroba(string displayName)
    {
        var result = User.Create(_validEmail, displayName, "hash", CreateTimeProvider());

        result.IsSuccess.Should().BeTrue();
        result.Value.DisplayName.Should().Be("ada");
    }

    [Fact] // CA-05
    public void Create_ComDisplayNameInformado_PreservaValorRecebidoApenasComTrim()
    {
        var result = User.Create(_validEmail, "  Ada Lovelace  ", "hash", CreateTimeProvider());

        result.IsSuccess.Should().BeTrue();
        result.Value.DisplayName.Should().Be("Ada Lovelace");
    }

    [Fact] // CA-06
    public void Create_UsuarioRecemCriado_TemIsActiveTrue()
    {
        var result = User.Create(_validEmail, "Ada Lovelace", "hash", CreateTimeProvider());

        result.IsSuccess.Should().BeTrue();
        result.Value.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Create_PreencheCreatedAtEUpdatedAtComOTimeProviderInjetado()
    {
        var timeProvider = CreateTimeProvider();

        var result = User.Create(_validEmail, "Ada Lovelace", "hash", timeProvider);

        var esperado = timeProvider.GetUtcNow().UtcDateTime;
        result.Value.CreatedAt.Should().Be(esperado);
        result.Value.UpdatedAt.Should().Be(esperado);
    }

    [Fact]
    public void Create_ComPasswordHashVazio_RetornaFalha()
    {
        var result = User.Create(_validEmail, "Ada Lovelace", string.Empty, CreateTimeProvider());

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Create_ComDisplayNameAcimaDe100Caracteres_RetornaFalha()
    {
        var nomeGrande = new string('a', User.DisplayNameMaxLength + 1);

        var result = User.Create(_validEmail, nomeGrande, "hash", CreateTimeProvider());

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Create_ComIdExplicito_UsaOIdInformado()
    {
        var id = Guid.NewGuid();

        var result = User.Create(id, _validEmail, "Ada Lovelace", "hash", CreateTimeProvider());

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(id);
    }

    [Fact] // CA-09
    public void Rename_ComNomeValido_AtualizaDisplayNameEUpdatedAt()
    {
        var timeProvider = CreateTimeProvider();
        var user = CreateValidUser(timeProvider);
        var updatedAtOriginal = user.UpdatedAt;

        timeProvider.Advance(TimeSpan.FromMinutes(5));
        var result = user.Rename("Novo Nome", timeProvider);

        result.IsSuccess.Should().BeTrue();
        user.DisplayName.Should().Be("Novo Nome");
        user.UpdatedAt.Should().Be(timeProvider.GetUtcNow().UtcDateTime);
        user.UpdatedAt.Should().BeAfter(updatedAtOriginal);
    }

    [Theory] // CA-09
    [InlineData("")]
    [InlineData("   ")]
    public void Rename_ComNomeVazioOuSoEspacos_RetornaFalhaENaoAlteraDisplayName(string nomeInvalido)
    {
        var timeProvider = CreateTimeProvider();
        var user = CreateValidUser(timeProvider);
        var nomeOriginal = user.DisplayName;

        var result = user.Rename(nomeInvalido, timeProvider);

        result.IsFailure.Should().BeTrue();
        user.DisplayName.Should().Be(nomeOriginal);
    }

    [Fact] // CA-09
    public void Rename_ComNomeAcimaDe100Caracteres_RetornaFalhaENaoAlteraUpdatedAt()
    {
        var timeProvider = CreateTimeProvider();
        var user = CreateValidUser(timeProvider);
        var updatedAtOriginal = user.UpdatedAt;
        var nomeGrande = new string('b', User.DisplayNameMaxLength + 1);

        timeProvider.Advance(TimeSpan.FromMinutes(5));
        var result = user.Rename(nomeGrande, timeProvider);

        result.IsFailure.Should().BeTrue();
        user.UpdatedAt.Should().Be(updatedAtOriginal, "uma alteração rejeitada não deve tocar UpdatedAt");
    }

    [Fact]
    public void Deactivate_MarcaIsActiveFalseEAtualizaUpdatedAt()
    {
        var timeProvider = CreateTimeProvider();
        var user = CreateValidUser(timeProvider);

        timeProvider.Advance(TimeSpan.FromMinutes(1));
        user.Deactivate(timeProvider);

        user.IsActive.Should().BeFalse();
        user.UpdatedAt.Should().Be(timeProvider.GetUtcNow().UtcDateTime);
    }

    [Fact]
    public void ChangePasswordHash_AtualizaHashEUpdatedAt()
    {
        var timeProvider = CreateTimeProvider();
        var user = CreateValidUser(timeProvider);

        timeProvider.Advance(TimeSpan.FromMinutes(1));
        user.ChangePasswordHash("novo-hash", timeProvider);

        user.PasswordHash.Should().Be("novo-hash");
        user.UpdatedAt.Should().Be(timeProvider.GetUtcNow().UtcDateTime);
    }

    [Fact]
    public void ChangePasswordHash_ComHashVazio_Lanca()
    {
        var timeProvider = CreateTimeProvider();
        var user = CreateValidUser(timeProvider);

        var act = () => user.ChangePasswordHash(string.Empty, timeProvider);

        act.Should().Throw<ArgumentException>();
    }

    private static User CreateValidUser(TimeProvider timeProvider) =>
        User.Create(_validEmail, "Ada Lovelace", "hash", timeProvider).Value;

    private static FakeTimeProvider CreateTimeProvider() =>
        new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
}
