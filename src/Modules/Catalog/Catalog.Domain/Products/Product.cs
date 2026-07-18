using System.Collections.ObjectModel;
using Catalog.Domain.Products.Events;
using Commerce.Domain;

namespace Catalog.Domain.Products;

public sealed class Product : AggregateRoot<ProductId>
{
    private readonly List<OptionDefinition> _optionDefinitions = [];
    private readonly ReadOnlyCollection<OptionDefinition> _readOnlyOptionDefinitions;

    private readonly List<ProductVariant> _variants = [];
    private readonly ReadOnlyCollection<ProductVariant> _readOnlyVariants;

    private Product(
        ProductId id,
        ProductName name,
        ProductSlug slug,
        ProductDescription description)
        : base(id)
    {
        Name = name;
        Slug = slug;
        Description = description;
        Status = ProductStatus.Draft;

        _readOnlyOptionDefinitions =
            _optionDefinitions.AsReadOnly();

        _readOnlyVariants =
            _variants.AsReadOnly();
    }

    public ProductName Name { get; private set; }

    public ProductSlug Slug { get; private set; }

    public ProductDescription Description { get; private set; }

    public ProductStatus Status { get; private set; }

    public DateTimeOffset? PublishedAtUtc { get; private set; }

    public DateTimeOffset? DiscontinuedAtUtc { get; private set; }

    public IReadOnlyList<OptionDefinition> OptionDefinitions =>
        _readOnlyOptionDefinitions;

    public IReadOnlyList<ProductVariant> Variants =>
        _readOnlyVariants;

    public static Product CreateDraft(
        ProductName name,
        ProductSlug slug,
        ProductDescription description,
        DateTimeOffset occurredAtUtc)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(slug);
        ArgumentNullException.ThrowIfNull(description);

        EnsureUtcTimestamp(
            occurredAtUtc,
            nameof(occurredAtUtc));

        var product = new Product(
            ProductId.Generate(),
            name,
            slug,
            description);

        product.RaiseDomainEvent(
            new ProductDraftCreatedDomainEvent(
                product.Id,
                occurredAtUtc));

        return product;
    }

    public Result ChangeName(ProductName name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (Status == ProductStatus.Discontinued)
        {
            return Result.Failure(
                ProductErrors.IsDiscontinued);
        }

        Name = name;

        return Result.Success();
    }

    public Result ChangeDescription(
        ProductDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);

        if (Status == ProductStatus.Discontinued)
        {
            return Result.Failure(
                ProductErrors.IsDiscontinued);
        }

        Description = description;

        return Result.Success();
    }

    public Result ChangeSlug(ProductSlug slug)
    {
        ArgumentNullException.ThrowIfNull(slug);

        if (Status == ProductStatus.Discontinued)
        {
            return Result.Failure(
                ProductErrors.IsDiscontinued);
        }

        if (Status != ProductStatus.Draft)
        {
            return Result.Failure(
                ProductErrors.SlugIsImmutable);
        }

        Slug = slug;

        return Result.Success();
    }

    public Result<ProductOptionId> DefineOption(
        OptionName name,
        int displayOrder)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (Status == ProductStatus.Discontinued)
        {
            return Result.Failure<ProductOptionId>(
                ProductErrors.IsDiscontinued);
        }

        if (Status != ProductStatus.Draft)
        {
            return Result.Failure<ProductOptionId>(
                ProductErrors.OptionSchemaFrozen);
        }

        if (_variants.Count > 0)
        {
            return Result.Failure<ProductOptionId>(
                ProductErrors.OptionSchemaLockedByVariants);
        }

        foreach (var optionDefinition in _optionDefinitions)
        {
            if (optionDefinition.HasSameNameAs(name))
            {
                return Result.Failure<ProductOptionId>(
                    ProductErrors.DuplicateOptionName);
            }

            if (optionDefinition.DisplayOrder == displayOrder)
            {
                return Result.Failure<ProductOptionId>(
                    ProductErrors.DuplicateOptionDisplayOrder);
            }
        }

        var definitionResult =
            OptionDefinition.Create(
                name,
                displayOrder);

        if (definitionResult.IsFailure)
        {
            return Result.Failure<ProductOptionId>(
                definitionResult.Error!);
        }

        var definition = definitionResult.Value;

        _optionDefinitions.Add(definition);

        _optionDefinitions.Sort(
            static (left, right) =>
                left.DisplayOrder.CompareTo(
                    right.DisplayOrder));

        return Result.Success(definition.Id);
    }

    public Result<ProductVariantId> AddVariant(
        Sku sku,
        VariantOptionCombination optionCombination,
        DateTimeOffset occurredAtUtc)
    {
        ArgumentNullException.ThrowIfNull(sku);
        ArgumentNullException.ThrowIfNull(optionCombination);

        EnsureUtcTimestamp(
            occurredAtUtc,
            nameof(occurredAtUtc));

        if (Status == ProductStatus.Discontinued)
        {
            return Result.Failure<ProductVariantId>(
                ProductErrors.IsDiscontinued);
        }

        var combinationError =
            GetOptionCombinationError(
                optionCombination);

        if (combinationError is not null)
        {
            return Result.Failure<ProductVariantId>(
                combinationError);
        }

        if (HasDuplicateSku(sku))
        {
            return Result.Failure<ProductVariantId>(
                ProductErrors.DuplicateSku);
        }

        if (HasDuplicateCombination(optionCombination))
        {
            return Result.Failure<ProductVariantId>(
                ProductErrors.DuplicateVariantCombination);
        }

        var variant = ProductVariant.Create(
            sku,
            optionCombination);

        _variants.Add(variant);

        RaiseDomainEvent(
            new ProductVariantAddedDomainEvent(
                Id,
                variant.Id,
                occurredAtUtc));

        return Result.Success(variant.Id);
    }

    public Result ChangeVariantSku(
        ProductVariantId variantId,
        Sku sku)
    {
        ArgumentNullException.ThrowIfNull(sku);

        if (Status == ProductStatus.Discontinued)
        {
            return Result.Failure(
                ProductErrors.IsDiscontinued);
        }

        var variant = FindVariant(variantId);

        if (variant is null)
        {
            return Result.Failure(
                ProductErrors.VariantNotFound);
        }

        if (variant.Status != ProductVariantStatus.Draft)
        {
            return variant.ChangeSku(sku);
        }

        if (HasDuplicateSku(
                sku,
                variant.Id))
        {
            return Result.Failure(
                ProductErrors.DuplicateSku);
        }

        return variant.ChangeSku(sku);
    }

    public Result ChangeVariantOptionCombination(
        ProductVariantId variantId,
        VariantOptionCombination optionCombination)
    {
        ArgumentNullException.ThrowIfNull(optionCombination);

        if (Status == ProductStatus.Discontinued)
        {
            return Result.Failure(
                ProductErrors.IsDiscontinued);
        }

        var variant = FindVariant(variantId);

        if (variant is null)
        {
            return Result.Failure(
                ProductErrors.VariantNotFound);
        }

        if (variant.Status != ProductVariantStatus.Draft)
        {
            return variant.ChangeOptionCombination(
                optionCombination);
        }

        var combinationError =
            GetOptionCombinationError(
                optionCombination);

        if (combinationError is not null)
        {
            return Result.Failure(
                combinationError);
        }

        if (HasDuplicateCombination(
                optionCombination,
                variant.Id))
        {
            return Result.Failure(
                ProductErrors.DuplicateVariantCombination);
        }

        return variant.ChangeOptionCombination(
            optionCombination);
    }

    public Result Publish(
        DateTimeOffset publishedAtUtc)
    {
        EnsureUtcTimestamp(
            publishedAtUtc,
            nameof(publishedAtUtc));

        if (Status == ProductStatus.Discontinued)
        {
            return Result.Failure(
                ProductErrors.IsDiscontinued);
        }

        if (Status == ProductStatus.Published)
        {
            return Result.Failure(
                ProductErrors.AlreadyPublished);
        }

        var variantsToActivate = _variants
            .Where(
                static variant =>
                    variant.Status ==
                    ProductVariantStatus.Draft)
            .ToArray();

        if (variantsToActivate.Length == 0)
        {
            return Result.Failure(
                ProductErrors.NoPublishableVariants);
        }

        foreach (var variant in variantsToActivate)
        {
            var combinationError =
                GetOptionCombinationError(
                    variant.OptionCombination);

            if (combinationError is not null)
            {
                return Result.Failure(
                    combinationError);
            }
        }

        foreach (var variant in variantsToActivate)
        {
            var activationResult =
                variant.Activate(publishedAtUtc);

            if (activationResult.IsFailure)
            {
                throw new InvalidOperationException(
                    "A validated draft variant could not be activated.");
            }
        }

        Status = ProductStatus.Published;
        PublishedAtUtc = publishedAtUtc;

        foreach (var variant in variantsToActivate)
        {
            RaiseDomainEvent(
                new ProductVariantActivatedDomainEvent(
                    Id,
                    variant.Id,
                    publishedAtUtc));
        }

        RaiseDomainEvent(
            new ProductPublishedDomainEvent(
                Id,
                publishedAtUtc));

        return Result.Success();
    }

    public Result ActivateVariant(
        ProductVariantId variantId,
        DateTimeOffset activatedAtUtc)
    {
        EnsureUtcTimestamp(
            activatedAtUtc,
            nameof(activatedAtUtc));

        if (Status == ProductStatus.Discontinued)
        {
            return Result.Failure(
                ProductErrors.IsDiscontinued);
        }

        if (Status != ProductStatus.Published)
        {
            return Result.Failure(
                ProductErrors.ProductMustBePublished);
        }

        if (PublishedAtUtc is { } publishedAtUtc &&
            activatedAtUtc < publishedAtUtc)
        {
            return Result.Failure(
                ProductErrors.InvalidActivationTimestamp);
        }

        var variant = FindVariant(variantId);

        if (variant is null)
        {
            return Result.Failure(
                ProductErrors.VariantNotFound);
        }

        var previousStatus = variant.Status;

        var result =
            variant.Activate(activatedAtUtc);

        if (result.IsSuccess &&
            previousStatus == ProductVariantStatus.Draft)
        {
            RaiseDomainEvent(
                new ProductVariantActivatedDomainEvent(
                    Id,
                    variant.Id,
                    activatedAtUtc));
        }

        return result;
    }

    public Result DiscontinueVariant(
        ProductVariantId variantId,
        DateTimeOffset discontinuedAtUtc)
    {
        EnsureUtcTimestamp(
            discontinuedAtUtc,
            nameof(discontinuedAtUtc));

        if (Status == ProductStatus.Discontinued)
        {
            return Result.Failure(
                ProductErrors.IsDiscontinued);
        }

        if (PublishedAtUtc is { } publishedAtUtc &&
            discontinuedAtUtc < publishedAtUtc)
        {
            return Result.Failure(
                ProductErrors.InvalidDiscontinuationTimestamp);
        }

        var variant = FindVariant(variantId);

        if (variant is null)
        {
            return Result.Failure(
                ProductErrors.VariantNotFound);
        }

        if (Status == ProductStatus.Published &&
            variant.Status == ProductVariantStatus.Active &&
            CountActiveVariants() == 1)
        {
            return Result.Failure(
                ProductErrors.LastActiveVariant);
        }

        var previousStatus = variant.Status;

        var result =
            variant.Discontinue(discontinuedAtUtc);

        if (result.IsSuccess &&
            previousStatus !=
            ProductVariantStatus.Discontinued)
        {
            RaiseDomainEvent(
                new ProductVariantDiscontinuedDomainEvent(
                    Id,
                    variant.Id,
                    discontinuedAtUtc));
        }

        return result;
    }

    public Result Discontinue(
        DateTimeOffset discontinuedAtUtc)
    {
        EnsureUtcTimestamp(
            discontinuedAtUtc,
            nameof(discontinuedAtUtc));

        if (Status == ProductStatus.Discontinued)
        {
            return Result.Success();
        }

        if (PublishedAtUtc is { } publishedAtUtc &&
            discontinuedAtUtc < publishedAtUtc)
        {
            return Result.Failure(
                ProductErrors.InvalidDiscontinuationTimestamp);
        }

        foreach (var variant in _variants)
        {
            if (variant.ActivatedAtUtc is { } activatedAtUtc &&
                discontinuedAtUtc < activatedAtUtc)
            {
                return Result.Failure(
                    ProductErrors.InvalidDiscontinuationTimestamp);
            }
        }

        var variantsToDiscontinue = _variants
            .Where(
                static variant =>
                    variant.Status !=
                    ProductVariantStatus.Discontinued)
            .ToArray();

        foreach (var variant in variantsToDiscontinue)
        {
            var result =
                variant.Discontinue(discontinuedAtUtc);

            if (result.IsFailure)
            {
                throw new InvalidOperationException(
                    "A validated product variant could not be discontinued.");
            }
        }

        Status = ProductStatus.Discontinued;
        DiscontinuedAtUtc = discontinuedAtUtc;

        foreach (var variant in variantsToDiscontinue)
        {
            RaiseDomainEvent(
                new ProductVariantDiscontinuedDomainEvent(
                    Id,
                    variant.Id,
                    discontinuedAtUtc));
        }

        RaiseDomainEvent(
            new ProductDiscontinuedDomainEvent(
                Id,
                discontinuedAtUtc));

        return Result.Success();
    }

    private ProductVariant? FindVariant(
        ProductVariantId variantId)
    {
        if (variantId.IsEmpty)
        {
            throw new ArgumentException(
                "A product variant identifier cannot be empty.",
                nameof(variantId));
        }

        return _variants.Find(
            variant => variant.Id == variantId);
    }

    private DomainError? GetOptionCombinationError(
        VariantOptionCombination optionCombination)
    {
        if (_optionDefinitions.Count == 0)
        {
            return optionCombination.IsEmpty
                ? null
                : ProductErrors.OptionsNotDefined;
        }

        foreach (var selection in optionCombination.Selections)
        {
            var optionExists = _optionDefinitions.Exists(
                definition =>
                    definition.Id == selection.OptionId);

            if (!optionExists)
            {
                return ProductErrors.UnknownOptionSelection;
            }
        }

        if (optionCombination.Count !=
            _optionDefinitions.Count)
        {
            return ProductErrors.MissingOptionSelection;
        }

        return null;
    }

    private bool HasDuplicateSku(
        Sku sku,
        ProductVariantId? excludedVariantId = null)
    {
        foreach (var variant in _variants)
        {
            if (excludedVariantId.HasValue &&
                variant.Id == excludedVariantId.Value)
            {
                continue;
            }

            if (variant.Sku == sku)
            {
                return true;
            }
        }

        return false;
    }

    private bool HasDuplicateCombination(
        VariantOptionCombination optionCombination,
        ProductVariantId? excludedVariantId = null)
    {
        foreach (var variant in _variants)
        {
            if (excludedVariantId.HasValue &&
                variant.Id == excludedVariantId.Value)
            {
                continue;
            }

            if (variant.Status ==
                ProductVariantStatus.Discontinued)
            {
                continue;
            }

            if (variant.OptionCombination.Equals(
                    optionCombination))
            {
                return true;
            }
        }

        return false;
    }

    private int CountActiveVariants()
    {
        var activeCount = 0;

        foreach (var variant in _variants)
        {
            if (variant.Status ==
                ProductVariantStatus.Active)
            {
                activeCount++;
            }
        }

        return activeCount;
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
