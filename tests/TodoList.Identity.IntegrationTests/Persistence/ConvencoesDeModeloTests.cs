using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TodoList.Identity.Infrastructure.Persistence.Conventions;
using Xunit;

namespace TodoList.Identity.IntegrationTests.Persistence;

/// <summary>
/// Trava as convenções de BE-02 no nível do <b>modelo</b> do EF, não do
/// comportamento observado. A distinção importa: os testes de comportamento
/// gravam valores que já nascem <c>Kind=Utc</c> (vêm do <c>TimeProvider</c>) e o
/// Npgsql devolve <c>Kind=Utc</c> por conta própria — passariam iguais com a
/// convenção desligada. Aqui a asserção é sobre o mapeamento em si, que é o que
/// a nota técnica de BE-02 realmente exige.
///
/// <para>Não precisa de Docker: monta o modelo sem abrir conexão.</para>
/// </summary>
public class ConvencoesDeModeloTests
{
    [Fact] // CA-05
    public void TodaPropriedadeDateTime_TemOConversorUtc_InclusiveAsAnulaveis()
    {
        using var context = CriarContextoSemConectar();

        var semConversor = context.Model.GetEntityTypes()
            .SelectMany(entityType => entityType.GetProperties())
            .Where(property => property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?))
            .Where(property => property.GetValueConverter() is not UtcDateTimeConverter)
            .Select(property => $"{property.DeclaringType.ClrType.Name}.{property.Name} ({property.ClrType.Name})")
            .ToList();

        semConversor.Should().BeEmpty(
            "toda data persistida passa pela conversão para UTC (BE-02, nota técnica) — se esta lista não está "
            + "vazia, a convenção não chegou ao modelo e um DateTime que não seja Kind=Utc vai estourar em runtime");
    }

    [Fact] // CA-05
    public void OConversorUtc_ConverteKindLocalEMarcaOQueVemDoBancoComoUtc()
    {
        var converter = new UtcDateTimeConverter();

        var instante = new DateTimeOffset(2026, 3, 15, 9, 30, 0, TimeSpan.FromHours(-3));

        var paraOBanco = (DateTime)converter.ConvertToProvider(instante.LocalDateTime)!;

        paraOBanco.Kind.Should().Be(DateTimeKind.Utc);
        paraOBanco.Should().Be(instante.UtcDateTime, "converter não pode deslocar o instante, só mudar o Kind");

        // O Npgsql devolve Unspecified por padrão; a volta precisa remarcar.
        var doBanco = (DateTime)converter.ConvertFromProvider(
            DateTime.SpecifyKind(instante.UtcDateTime, DateTimeKind.Unspecified))!;

        doBanco.Kind.Should().Be(DateTimeKind.Utc);
        doBanco.Should().Be(instante.UtcDateTime);
    }

    private static ProbeDbContext CriarContextoSemConectar()
    {
        // Npgsql como provider (é o alvo real, D-17), mas nenhuma conexão é
        // aberta: construir o Model não fala com o banco.
        var options = new DbContextOptionsBuilder<ProbeDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=5432;Database=nao-conecta;Username=x;Password=y")
            .Options;

        return new ProbeDbContext(options);
    }
}
