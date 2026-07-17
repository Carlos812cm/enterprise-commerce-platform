using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Commerce.Domain;

namespace Catalog.Domain.Products;

public sealed class VariantOptionCombination :
    IEquatable<VariantOptionCombination>
{
    private static readonly DomainError DuplicateSelectionError =
        DomainError.Validation(
            "Catalog.Variant.DuplicateOptionSelection",
            "A variant cannot contain more than one selection for the same option.");

    private readonly OptionSelection[] _orderedSelections;
    private readonly ReadOnlyCollection<OptionSelection> _readOnlySelections;

    private VariantOptionCombination(
        OptionSelection[] orderedSelections)
    {
        _orderedSelections = orderedSelections;
        _readOnlySelections =
            Array.AsReadOnly(_orderedSelections);

        CanonicalKey =
            CreateCanonicalKey(_orderedSelections);
    }

    public static VariantOptionCombination Empty { get; } =
        new(Array.Empty<OptionSelection>());

    public IReadOnlyList<OptionSelection> Selections =>
        _readOnlySelections;

    public int Count => _orderedSelections.Length;

    public bool IsEmpty => Count == 0;

    public string CanonicalKey { get; }

    public static Result<VariantOptionCombination> Create(
        IEnumerable<OptionSelection> selections)
    {
        ArgumentNullException.ThrowIfNull(selections);

        var snapshot = selections.ToArray();
        var optionIds = new HashSet<ProductOptionId>();

        foreach (var selection in snapshot)
        {
            if (selection is null)
            {
                throw new ArgumentException(
                    "An option selection cannot be null.",
                    nameof(selections));
            }

            if (!optionIds.Add(selection.OptionId))
            {
                return Result.Failure<VariantOptionCombination>(
                    DuplicateSelectionError);
            }
        }

        if (snapshot.Length == 0)
        {
            return Result.Success(Empty);
        }

        Array.Sort(
            snapshot,
            static (left, right) =>
                left.OptionId.Value.CompareTo(
                    right.OptionId.Value));

        return Result.Success(
            new VariantOptionCombination(snapshot));
    }

    public bool Equals(
        VariantOptionCombination? other)
    {
        return other is not null &&
            _orderedSelections
                .AsSpan()
                .SequenceEqual(
                    other._orderedSelections);
    }

    public override bool Equals(object? obj)
    {
        return obj is VariantOptionCombination combination &&
            Equals(combination);
    }

    public override int GetHashCode()
    {
        return StringComparer.Ordinal.GetHashCode(
            CanonicalKey);
    }

    public override string ToString()
    {
        return CanonicalKey;
    }

    private static string CreateCanonicalKey(
        OptionSelection[] selections)
    {
        var payloadBuilder =
            new StringBuilder(selections.Length * 96);

        foreach (var selection in selections)
        {
            if (payloadBuilder.Length > 0)
            {
                payloadBuilder.Append('|');
            }

            payloadBuilder.Append(
                selection.OptionId.Value.ToString(
                    "N",
                    CultureInfo.InvariantCulture));

            payloadBuilder.Append(':');

            var valueBytes = Encoding.UTF8.GetBytes(
                selection.ComparisonValue);

            payloadBuilder.Append(
                Convert.ToBase64String(valueBytes));
        }

        var payloadBytes = Encoding.UTF8.GetBytes(
            payloadBuilder.ToString());

        return Convert.ToHexString(
            SHA256.HashData(payloadBytes));
    }
}
