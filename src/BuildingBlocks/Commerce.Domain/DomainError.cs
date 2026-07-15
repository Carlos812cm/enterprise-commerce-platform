namespace Commerce.Domain;

public sealed record DomainError
{
    private DomainError(
        string code,
        string description,
        ErrorType type)
    {
        Code = Normalize(code, nameof(code));
        Description = Normalize(description, nameof(description));
        Type = type;
    }

    public string Code { get; }

    public string Description { get; }

    public ErrorType Type { get; }

    public static DomainError Failure(
        string code,
        string description)
    {
        return new DomainError(
            code,
            description,
            ErrorType.Failure);
    }

    public static DomainError Validation(
        string code,
        string description)
    {
        return new DomainError(
            code,
            description,
            ErrorType.Validation);
    }

    public static DomainError Conflict(
        string code,
        string description)
    {
        return new DomainError(
            code,
            description,
            ErrorType.Conflict);
    }

    public static DomainError NotFound(
        string code,
        string description)
    {
        return new DomainError(
            code,
            description,
            ErrorType.NotFound);
    }

    private static string Normalize(
        string value,
        string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            value,
            parameterName);

        return value.Trim();
    }
}
