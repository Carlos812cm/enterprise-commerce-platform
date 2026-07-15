using Catalog.Domain.Internal;
using Commerce.Domain;

namespace Catalog.Domain.Products;

public sealed record ProductName
{
    public const int MaximumLength = 200;

    private static readonly DomainError RequiredError =
        DomainError.Validation(
            "Catalog.Product.NameRequired",
            "The product name is required.");

    private static readonly DomainError TooLongError =
        DomainError.Validation(
            "Catalog.Product.NameTooLong",
            "The product name cannot exceed 200 characters.");

    private ProductName(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static Result<ProductName> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Failure<ProductName>(
                RequiredError);
        }

        var normalizedValue =
            CatalogTextNormalizer.CollapseWhitespace(value);

        if (normalizedValue.Length > MaximumLength)
        {
            return Result.Failure<ProductName>(
                TooLongError);
        }

        return Result.Success(
            new ProductName(normalizedValue));
    }

    public override string ToString()
    {
        return Value;
    }
}
