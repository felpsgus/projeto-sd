using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace TodoList.Tasks.Infrastructure.Persistence.Conventions;

/// <summary>
/// Garante que todo <see cref="DateTime"/> persistido está em UTC (BE-02,
/// nota técnica — mapeia para <c>timestamptz</c> no Postgres). Na escrita:
/// <see cref="DateTimeKind.Local"/> é convertido; <see cref="DateTimeKind.Unspecified"/>
/// é tratado como já sendo UTC (decisão de projeto — ver relatório de BE-02),
/// em vez de lançar, para não travar código que só esqueceu de marcar o
/// <c>Kind</c>. Na leitura, o valor volta sempre com <c>Kind=Utc</c>, porque o
/// driver Npgsql devolve <c>Unspecified</c> por padrão.
/// </summary>
public sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter()
        : base(
            toProvider => ToUtc(toProvider),
            fromProvider => DateTime.SpecifyKind(fromProvider, DateTimeKind.Utc))
    {
    }

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
