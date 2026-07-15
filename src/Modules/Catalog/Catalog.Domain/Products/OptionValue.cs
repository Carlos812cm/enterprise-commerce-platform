using Catalog.Domain.Internal;
using Commerce.Domain;

namespace Catalog.Domain.Products;

public sealed record OptionValue
{
    public const int MaximumLength = 100;

    private static readonly DomainError RequiredError =
        DomainError.Validation(
            "Catalog.Variant.OptionValueRequired",
            "The option value is required.");

    private static readonly DomainError TooLongError =
        DomainError.Validation(
            "Catalog.Variant.OptionValueTooLong",
            "The option value cannot exceed 100 characters.");

    private OptionValue(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static Result<OptionValue> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Failure<OptionValue>(
                RequiredError);
        }

        var normalizedValue =
            CatalogTextNormalizer.CollapseWhitespace(value);

        if (normalizedValue.Length > MaximumLength)
        {
            return Result.Failure<OptionValue>(
                TooLongError);
        }

        return Result.Success(
            new OptionValue(normalizedValue));
    }

    public override string ToString()
    {
        return Value;
    }
}
