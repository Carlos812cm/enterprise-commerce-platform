using Catalog.Domain.Internal;
using Commerce.Domain;

namespace Catalog.Domain.Products;

public sealed class OptionDefinition : Entity<ProductOptionId>
{
    private static readonly DomainError InvalidDisplayOrderError =
        DomainError.Validation(
            "Catalog.Product.OptionDisplayOrderInvalid",
            "The option display order cannot be negative.");

    private readonly string _nameComparisonKey;

    private OptionDefinition(
        ProductOptionId id,
        OptionName name,
        int displayOrder)
        : base(id)
    {
        Name = name;
        DisplayOrder = displayOrder;
        _nameComparisonKey =
            CatalogTextNormalizer.CreateComparisonKey(name.Value);
    }

    public OptionName Name { get; }

    public int DisplayOrder { get; }

    public static Result<OptionDefinition> Create(
        OptionName name,
        int displayOrder)
    {
        return Create(
            ProductOptionId.Generate(),
            name,
            displayOrder);
    }

    public static Result<OptionDefinition> Create(
        ProductOptionId id,
        OptionName name,
        int displayOrder)
    {
        if (id.IsEmpty)
        {
            throw new ArgumentException(
                "A product option identifier cannot be empty.",
                nameof(id));
        }

        ArgumentNullException.ThrowIfNull(name);

        if (displayOrder < 0)
        {
            return Result.Failure<OptionDefinition>(
                InvalidDisplayOrderError);
        }

        return Result.Success(
            new OptionDefinition(
                id,
                name,
                displayOrder));
    }

    public bool HasSameNameAs(OptionName name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var comparisonKey =
            CatalogTextNormalizer.CreateComparisonKey(name.Value);

        return StringComparer.Ordinal.Equals(
            _nameComparisonKey,
            comparisonKey);
    }
}
