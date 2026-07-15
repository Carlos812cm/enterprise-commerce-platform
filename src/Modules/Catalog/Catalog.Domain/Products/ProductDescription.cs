using Commerce.Domain;

namespace Catalog.Domain.Products;

public sealed record ProductDescription
{
    public const int MaximumLength = 4_000;

    private static readonly DomainError TooLongError =
        DomainError.Validation(
            "Catalog.Product.DescriptionTooLong",
            "The product description cannot exceed 4000 characters.");

    private ProductDescription(string value)
    {
        Value = value;
    }

    public static ProductDescription Empty { get; } =
        new(string.Empty);

    public string Value { get; }

    public bool IsEmpty => Value.Length == 0;

    public static Result<ProductDescription> Create(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Success(Empty);
        }

        var normalizedValue = value
            .ReplaceLineEndings("\n")
            .Trim();

        if (normalizedValue.Length > MaximumLength)
        {
            return Result.Failure<ProductDescription>(
                TooLongError);
        }

        return Result.Success(
            new ProductDescription(normalizedValue));
    }

    public override string ToString()
    {
        return Value;
    }
}
