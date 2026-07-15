using Commerce.Domain;

namespace Catalog.Domain.Products;

public sealed record Sku
{
    public const int MaximumLength = 64;

    private static readonly DomainError RequiredError =
        DomainError.Validation(
            "Catalog.Variant.SkuRequired",
            "The SKU is required.");

    private static readonly DomainError TooLongError =
        DomainError.Validation(
            "Catalog.Variant.SkuTooLong",
            "The SKU cannot exceed 64 characters.");

    private static readonly DomainError InvalidFormatError =
        DomainError.Validation(
            "Catalog.Variant.InvalidSku",
            "The SKU must contain ASCII letters, numbers, periods, underscores or hyphens, and must start and end with a letter or number.");

    private Sku(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static Result<Sku> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Failure<Sku>(
                RequiredError);
        }

        var normalizedValue = value
            .Trim()
            .ToUpperInvariant();

        if (normalizedValue.Length > MaximumLength)
        {
            return Result.Failure<Sku>(
                TooLongError);
        }

        if (!IsValidSku(normalizedValue))
        {
            return Result.Failure<Sku>(
                InvalidFormatError);
        }

        return Result.Success(
            new Sku(normalizedValue));
    }

    public override string ToString()
    {
        return Value;
    }

    private static bool IsValidSku(string value)
    {
        if (!IsAsciiAlphaNumeric(value[0]) ||
            !IsAsciiAlphaNumeric(value[^1]))
        {
            return false;
        }

        foreach (var character in value)
        {
            if (IsAsciiAlphaNumeric(character))
            {
                continue;
            }

            if (character != '.' &&
                character != '_' &&
                character != '-')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAsciiAlphaNumeric(char value)
    {
        return value is >= 'A' and <= 'Z' or
            >= '0' and <= '9';
    }
}
