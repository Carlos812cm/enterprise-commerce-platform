using Commerce.Domain;

namespace Catalog.Domain.Products;

public sealed record ProductSlug
{
    public const int MinimumLength = 3;
    public const int MaximumLength = 200;

    private static readonly DomainError RequiredError =
        DomainError.Validation(
            "Catalog.Product.SlugRequired",
            "The product slug is required.");

    private static readonly DomainError TooShortError =
        DomainError.Validation(
            "Catalog.Product.SlugTooShort",
            "The product slug must contain at least 3 characters.");

    private static readonly DomainError TooLongError =
        DomainError.Validation(
            "Catalog.Product.SlugTooLong",
            "The product slug cannot exceed 200 characters.");

    private static readonly DomainError InvalidFormatError =
        DomainError.Validation(
            "Catalog.Product.InvalidSlug",
            "The product slug must contain lowercase ASCII letters, numbers and single hyphens.");

    private ProductSlug(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static Result<ProductSlug> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Failure<ProductSlug>(
                RequiredError);
        }

        var normalizedValue = value.Trim();

        if (normalizedValue.Length < MinimumLength)
        {
            return Result.Failure<ProductSlug>(
                TooShortError);
        }

        if (normalizedValue.Length > MaximumLength)
        {
            return Result.Failure<ProductSlug>(
                TooLongError);
        }

        if (!IsCanonicalSlug(normalizedValue))
        {
            return Result.Failure<ProductSlug>(
                InvalidFormatError);
        }

        return Result.Success(
            new ProductSlug(normalizedValue));
    }

    public override string ToString()
    {
        return Value;
    }

    private static bool IsCanonicalSlug(string value)
    {
        if (value[0] == '-' ||
            value[^1] == '-')
        {
            return false;
        }

        var previousWasHyphen = false;

        foreach (var character in value)
        {
            if (IsAsciiLowercaseLetter(character) ||
                IsAsciiDigit(character))
            {
                previousWasHyphen = false;
                continue;
            }

            if (character == '-' &&
                !previousWasHyphen)
            {
                previousWasHyphen = true;
                continue;
            }

            return false;
        }

        return true;
    }

    private static bool IsAsciiLowercaseLetter(char value)
    {
        return value is >= 'a' and <= 'z';
    }

    private static bool IsAsciiDigit(char value)
    {
        return value is >= '0' and <= '9';
    }
}
