using Catalog.Domain.Internal;

namespace Catalog.Domain.Products;

public sealed class OptionSelection :
    IEquatable<OptionSelection>
{
    private readonly string _valueComparisonKey;

    private OptionSelection(
        ProductOptionId optionId,
        OptionValue value)
    {
        OptionId = optionId;
        Value = value;
        _valueComparisonKey =
            CatalogTextNormalizer.CreateComparisonKey(value.Value);
    }

    public ProductOptionId OptionId { get; }

    public OptionValue Value { get; }

    internal string ComparisonValue =>
        _valueComparisonKey;

    public static OptionSelection Create(
        ProductOptionId optionId,
        OptionValue value)
    {
        if (optionId.IsEmpty)
        {
            throw new ArgumentException(
                "A product option identifier cannot be empty.",
                nameof(optionId));
        }

        ArgumentNullException.ThrowIfNull(value);

        return new OptionSelection(
            optionId,
            value);
    }

    public bool Equals(OptionSelection? other)
    {
        return other is not null &&
            OptionId == other.OptionId &&
            StringComparer.Ordinal.Equals(
                _valueComparisonKey,
                other._valueComparisonKey);
    }

    public override bool Equals(object? obj)
    {
        return obj is OptionSelection selection &&
            Equals(selection);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(
            OptionId,
            StringComparer.Ordinal.GetHashCode(
                _valueComparisonKey));
    }

    public override string ToString()
    {
        return string.Concat(
            OptionId.ToString(),
            "=",
            Value.Value);
    }
}
