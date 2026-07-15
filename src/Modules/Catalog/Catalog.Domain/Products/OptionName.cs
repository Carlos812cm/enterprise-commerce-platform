using Catalog.Domain.Internal;
using Commerce.Domain;

namespace Catalog.Domain.Products;

public sealed record OptionName
{
    public const int MaximumLength = 64;

    private static readonly DomainError RequiredError =
        DomainError.Validation(
            "Catalog.Product.OptionNameRequired",
            "The option name is required.");

    private static readonly DomainError TooLongError =
        DomainError.Validation(
            "Catalog.Product.OptionNameTooLong",
            "The option name cannot exceed 64 characters.");

    private OptionName(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static Result<OptionName> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Failure<OptionName>(
                RequiredError);
        }

        var normalizedValue =
            CatalogTextNormalizer.CollapseWhitespace(value);

        if (normalizedValue.Length > MaximumLength)
        {
            return Result.Failure<OptionName>(
                TooLongError);
        }

        return Result.Success(
            new OptionName(normalizedValue));
    }

    public override string ToString()
    {
        return Value;
    }
}
