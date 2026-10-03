using System.ComponentModel.DataAnnotations;

namespace TodoList.SharedKernel.Retention;

/// <summary>Seção <c>Retention</c> (BE-23): liga/desliga e cadência do expurgo, validada na inicialização.</summary>
public sealed class RetentionOptions
{
    public const string SectionName = "Retention";

    public bool Enabled { get; init; } = true;

    [Range(1, 8760, ErrorMessage = "Retention:IntervalHours deve estar entre 1 e 8760.")]
    public int IntervalHours { get; init; } = 24;

    [Range(1, 10000, ErrorMessage = "Retention:BatchSize deve estar entre 1 e 10000.")]
    public int BatchSize { get; init; } = 500;
}
