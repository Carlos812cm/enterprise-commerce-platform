namespace Commerce.Domain;

public sealed class Result<T> : Result
{
    private readonly T _value;

    internal Result(
        T value,
        bool isSuccess,
        DomainError? error)
        : base(isSuccess, error)
    {
        _value = value;
    }

    public T Value
    {
        get
        {
            if (IsFailure)
            {
                throw new InvalidOperationException(
                    "The value of a failed result cannot be accessed.");
            }

            return _value;
        }
    }
}
