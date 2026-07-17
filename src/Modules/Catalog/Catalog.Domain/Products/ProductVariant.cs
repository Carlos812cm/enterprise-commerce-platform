using Commerce.Domain;

namespace Catalog.Domain.Products;

public sealed class ProductVariant :
    Entity<ProductVariantId>
{
    private ProductVariant(
        ProductVariantId id,
        Sku sku,
        VariantOptionCombination optionCombination)
        : base(id)
    {
        Sku = sku;
        OptionCombination = optionCombination;
        Status = ProductVariantStatus.Draft;
    }

    public Sku Sku { get; private set; }

    public VariantOptionCombination OptionCombination
    {
        get;
        private set;
    }

    public ProductVariantStatus Status { get; private set; }

    public DateTimeOffset? ActivatedAtUtc { get; private set; }

    public DateTimeOffset? DiscontinuedAtUtc { get; private set; }

    internal static ProductVariant Create(
        Sku sku,
        VariantOptionCombination optionCombination)
    {
        return Create(
            ProductVariantId.Generate(),
            sku,
            optionCombination);
    }

    internal static ProductVariant Create(
        ProductVariantId id,
        Sku sku,
        VariantOptionCombination optionCombination)
    {
        if (id.IsEmpty)
        {
            throw new ArgumentException(
                "A product variant identifier cannot be empty.",
                nameof(id));
        }

        ArgumentNullException.ThrowIfNull(sku);
        ArgumentNullException.ThrowIfNull(optionCombination);

        return new ProductVariant(
            id,
            sku,
            optionCombination);
    }

    internal Result ChangeSku(Sku sku)
    {
        ArgumentNullException.ThrowIfNull(sku);

        if (Status == ProductVariantStatus.Discontinued)
        {
            return Result.Failure(
                ProductVariantErrors.IsDiscontinued);
        }

        if (Status != ProductVariantStatus.Draft)
        {
            return Result.Failure(
                ProductVariantErrors.SkuIsImmutable);
        }

        Sku = sku;

        return Result.Success();
    }

    internal Result ChangeOptionCombination(
        VariantOptionCombination optionCombination)
    {
        ArgumentNullException.ThrowIfNull(optionCombination);

        if (Status == ProductVariantStatus.Discontinued)
        {
            return Result.Failure(
                ProductVariantErrors.IsDiscontinued);
        }

        if (Status != ProductVariantStatus.Draft)
        {
            return Result.Failure(
                ProductVariantErrors.OptionsAreImmutable);
        }

        OptionCombination = optionCombination;

        return Result.Success();
    }

    internal Result Activate(
        DateTimeOffset activatedAtUtc)
    {
        EnsureUtcTimestamp(
            activatedAtUtc,
            nameof(activatedAtUtc));

        if (Status == ProductVariantStatus.Active)
        {
            return Result.Success();
        }

        if (Status == ProductVariantStatus.Discontinued)
        {
            return Result.Failure(
                ProductVariantErrors.CannotActivateDiscontinued);
        }

        Status = ProductVariantStatus.Active;
        ActivatedAtUtc = activatedAtUtc;

        return Result.Success();
    }

    internal Result Discontinue(
        DateTimeOffset discontinuedAtUtc)
    {
        EnsureUtcTimestamp(
            discontinuedAtUtc,
            nameof(discontinuedAtUtc));

        if (Status == ProductVariantStatus.Discontinued)
        {
            return Result.Success();
        }

        if (ActivatedAtUtc is { } activatedAtUtc &&
            discontinuedAtUtc < activatedAtUtc)
        {
            return Result.Failure(
                ProductVariantErrors.DiscontinuedBeforeActivation);
        }

        Status = ProductVariantStatus.Discontinued;
        DiscontinuedAtUtc = discontinuedAtUtc;

        return Result.Success();
    }

    private static void EnsureUtcTimestamp(
        DateTimeOffset timestamp,
        string parameterName)
    {
        if (timestamp == default)
        {
            throw new ArgumentException(
                "The timestamp must contain a value.",
                parameterName);
        }

        if (timestamp.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "The timestamp must use the UTC offset.",
                parameterName);
        }
    }
}
