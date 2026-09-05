namespace TodoList.SharedKernel;

/// <summary>
/// Resultado de uma operação que pode falhar por motivo de negócio (seção 2.1
/// das convenções: erro esperado é valor de retorno, não exceção). Imutável.
/// </summary>
public class Result
{
    private readonly Error? _error;

    protected Result(bool isSuccess, Error? error)
    {
        if (isSuccess && error is not null)
        {
            throw new InvalidOperationException("Um resultado de sucesso não pode carregar um Error.");
        }

        if (!isSuccess && error is null)
        {
            throw new InvalidOperationException("Um resultado de falha precisa de um Error.");
        }

        IsSuccess = isSuccess;
        _error = error;
    }

    /// <summary>Indica se a operação teve sucesso.</summary>
    public bool IsSuccess { get; }

    /// <summary>Indica se a operação falhou.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// O erro da falha. Acessar em um resultado de sucesso é bug de
    /// programação (CA-01) — lança <see cref="InvalidOperationException"/>.
    /// </summary>
    public Error Error => _error ?? throw new InvalidOperationException(
        "Result.Error não pode ser acessado em um resultado de sucesso.");

    /// <summary>Cria um resultado de sucesso sem valor.</summary>
    public static Result Success() => new(true, null);

    /// <summary>Cria um resultado de falha com o <paramref name="error"/> informado.</summary>
    public static Result Failure(Error error) => new(false, error);

    /// <summary>Cria um resultado de sucesso com <paramref name="value"/>.</summary>
    public static Result<TValue> Success<TValue>(TValue value) => new(value, true, null);

    /// <summary>Cria um resultado de falha tipado com o <paramref name="error"/> informado.</summary>
    public static Result<TValue> Failure<TValue>(Error error) => new(default, false, error);
}

/// <summary>
/// <see cref="Result"/> que carrega um valor de sucesso do tipo <typeparamref name="TValue"/>.
/// </summary>
public sealed class Result<TValue> : Result
{
    private readonly TValue? _value;

    internal Result(TValue? value, bool isSuccess, Error? error)
        : base(isSuccess, error)
    {
        _value = value;
    }

    /// <summary>
    /// O valor de sucesso. Acessar em um resultado de falha é bug de
    /// programação (CA-01) — lança <see cref="InvalidOperationException"/>.
    /// </summary>
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Result<T>.Value não pode ser acessado em um resultado de falha.");

    /// <summary>Conveniência: <c>TValue</c> vira um <see cref="Result{TValue}"/> de sucesso implicitamente.</summary>
    public static implicit operator Result<TValue>(TValue value) => Success(value);
}
