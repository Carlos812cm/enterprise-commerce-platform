namespace Commerce.Domain;

public class Result
{
    private protected Result(
        bool isSuccess,
        DomainError? error)
    {
        if (isSuccess && error is not null)
        {
            throw new ArgumentException(
                "A successful result cannot contain an error.",
                nameof(error));
        }

        if (!isSuccess && error is null)
        {
            throw new ArgumentException(
                "A failed result must contain an error.",
                nameof(error));
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public DomainError? Error { get; }

    public static Result Success()
    {
        return new Result(
            isSuccess: true,
            error: null);
    }

    public static Result Failure(DomainError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return new Result(
            isSuccess: false,
            error);
    }

    public static Result<T> Success<T>(T value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new Result<T>(
            value,
            isSuccess: true,
            error: null);
    }

    public static Result<T> Failure<T>(DomainError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return new Result<T>(
            default!,
            isSuccess: false,
            error);
    }
}