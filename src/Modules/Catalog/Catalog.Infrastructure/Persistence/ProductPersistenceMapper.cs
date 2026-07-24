using Catalog.Domain.Internal;
using Catalog.Domain.Products;
using Catalog.Infrastructure.Persistence.Records;
using Commerce.Domain;

namespace Catalog.Infrastructure.Persistence;

internal static class ProductPersistenceMapper
{
    public static ProductRecord ToRecord(
        Product product)
    {
        ArgumentNullException.ThrowIfNull(product);

        var record = new ProductRecord
        {
            Id = product.Id.Value,
            Version = 1
        };

        CopyRootState(
            product,
            record);

        foreach (var option in product.OptionDefinitions)
        {
            record.OptionDefinitions.Add(
                CreateOptionRecord(
                    product.Id,
                    option));
        }

        foreach (var variant in product.Variants)
        {
            record.Variants.Add(
                CreateVariantRecord(
                    product.Id,
                    variant));
        }

        return record;
    }

    public static Product ToDomain(
        ProductRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var optionDefinitions = record.OptionDefinitions
            .OrderBy(
                static option =>
                    option.DisplayOrder)
            .Select(CreateOptionDefinition)
            .ToArray();

        var variants = record.Variants
            .Select(CreateProductVariant)
            .ToArray();

        return Product.Rehydrate(
            ProductId.Create(record.Id),
            GetRequiredValue(
                ProductName.Create(record.Name),
                nameof(ProductName)),
            GetRequiredValue(
                ProductSlug.Create(record.Slug),
                nameof(ProductSlug)),
            GetRequiredValue(
                ProductDescription.Create(
                    record.Description),
                nameof(ProductDescription)),
            record.Status,
            record.PublishedAtUtc,
            record.DiscontinuedAtUtc,
            optionDefinitions,
            variants);
    }

    public static void Apply(
        Product product,
        ProductRecord record)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(record);

        if (product.Id.Value != record.Id)
        {
            throw new InvalidOperationException(
                "The aggregate and persistence record identifiers do not match.");
        }

        CopyRootState(
            product,
            record);

        record.Version =
            checked(record.Version + 1);

        SynchronizeOptions(
            product,
            record);

        SynchronizeVariants(
            product,
            record);
    }

    private static void CopyRootState(
        Product product,
        ProductRecord record)
    {
        record.Name = product.Name.Value;
        record.Slug = product.Slug.Value;
        record.Description =
            product.Description.Value;

        record.Status = product.Status;
        record.PublishedAtUtc =
            product.PublishedAtUtc;

        record.DiscontinuedAtUtc =
            product.DiscontinuedAtUtc;
    }

    private static ProductOptionRecord CreateOptionRecord(
        ProductId productId,
        OptionDefinition option)
    {
        return new ProductOptionRecord
        {
            Id = option.Id.Value,
            ProductId = productId.Value,
            Name = option.Name.Value,
            NameKey =
                CatalogTextNormalizer
                    .CreateComparisonKey(
                        option.Name.Value),
            DisplayOrder = option.DisplayOrder
        };
    }

    private static ProductVariantRecord CreateVariantRecord(
        ProductId productId,
        ProductVariant variant)
    {
        var record = new ProductVariantRecord
        {
            Id = variant.Id.Value,
            ProductId = productId.Value,
            Sku = variant.Sku.Value,
            Status = variant.Status,
            OptionCombinationKey =
                variant.OptionCombination.CanonicalKey,
            ActivatedAtUtc =
                variant.ActivatedAtUtc,
            DiscontinuedAtUtc =
                variant.DiscontinuedAtUtc
        };

        foreach (var selection in
            variant.OptionCombination.Selections)
        {
            record.OptionSelections.Add(
                new ProductVariantOptionRecord
                {
                    ProductId = productId.Value,
                    ProductVariantId =
                        variant.Id.Value,
                    OptionId =
                        selection.OptionId.Value,
                    Value =
                        selection.Value.Value
                });
        }

        return record;
    }

    private static OptionDefinition CreateOptionDefinition(
        ProductOptionRecord record)
    {
        return GetRequiredValue(
            OptionDefinition.Create(
                ProductOptionId.Create(record.Id),
                GetRequiredValue(
                    OptionName.Create(record.Name),
                    nameof(OptionName)),
                record.DisplayOrder),
            nameof(OptionDefinition));
    }

    private static ProductVariant CreateProductVariant(
        ProductVariantRecord record)
    {
        var selections = record.OptionSelections
            .OrderBy(
                static selection =>
                    selection.OptionId)
            .Select(
                selection =>
                    OptionSelection.Create(
                        ProductOptionId.Create(
                            selection.OptionId),
                        GetRequiredValue(
                            OptionValue.Create(
                                selection.Value),
                            nameof(OptionValue))))
            .ToArray();

        var combination =
            GetRequiredValue(
                VariantOptionCombination.Create(
                    selections),
                nameof(VariantOptionCombination));

        if (!StringComparer.Ordinal.Equals(
                combination.CanonicalKey,
                record.OptionCombinationKey))
        {
            throw new InvalidOperationException(
                "The persisted variant combination key does not match its selections.");
        }

        return ProductVariant.Rehydrate(
            ProductVariantId.Create(record.Id),
            GetRequiredValue(
                Sku.Create(record.Sku),
                nameof(Sku)),
            combination,
            record.Status,
            record.ActivatedAtUtc,
            record.DiscontinuedAtUtc);
    }

    private static void SynchronizeOptions(
        Product product,
        ProductRecord record)
    {
        foreach (var persistedOption in
            record.OptionDefinitions)
        {
            if (!product.OptionDefinitions.Any(
                    option =>
                        option.Id.Value ==
                        persistedOption.Id))
            {
                throw new InvalidOperationException(
                    "Removing persisted product options is not supported.");
            }
        }

        foreach (var option in
            product.OptionDefinitions)
        {
            var optionRecord =
                record.OptionDefinitions
                    .SingleOrDefault(
                        persisted =>
                            persisted.Id ==
                            option.Id.Value);

            if (optionRecord is null)
            {
                record.OptionDefinitions.Add(
                    CreateOptionRecord(
                        product.Id,
                        option));

                continue;
            }

            optionRecord.Name =
                option.Name.Value;

            optionRecord.NameKey =
                CatalogTextNormalizer
                    .CreateComparisonKey(
                        option.Name.Value);

            optionRecord.DisplayOrder =
                option.DisplayOrder;
        }
    }

    private static void SynchronizeVariants(
        Product product,
        ProductRecord record)
    {
        foreach (var persistedVariant in
            record.Variants)
        {
            if (!product.Variants.Any(
                    variant =>
                        variant.Id.Value ==
                        persistedVariant.Id))
            {
                throw new InvalidOperationException(
                    "Removing persisted product variants is not supported.");
            }
        }

        foreach (var variant in product.Variants)
        {
            var variantRecord =
                record.Variants
                    .SingleOrDefault(
                        persisted =>
                            persisted.Id ==
                            variant.Id.Value);

            if (variantRecord is null)
            {
                record.Variants.Add(
                    CreateVariantRecord(
                        product.Id,
                        variant));

                continue;
            }

            variantRecord.Sku =
                variant.Sku.Value;

            variantRecord.Status =
                variant.Status;

            variantRecord.OptionCombinationKey =
                variant.OptionCombination
                    .CanonicalKey;

            variantRecord.ActivatedAtUtc =
                variant.ActivatedAtUtc;

            variantRecord.DiscontinuedAtUtc =
                variant.DiscontinuedAtUtc;

            SynchronizeSelections(
                product.Id,
                variant,
                variantRecord);
        }
    }

    private static void SynchronizeSelections(
        ProductId productId,
        ProductVariant variant,
        ProductVariantRecord record)
    {
        foreach (var persistedSelection in
            record.OptionSelections)
        {
            if (!variant.OptionCombination.Selections.Any(
                    selection =>
                        selection.OptionId.Value ==
                        persistedSelection.OptionId))
            {
                throw new InvalidOperationException(
                    "Changing the option schema of a persisted variant is not supported.");
            }
        }

        foreach (var selection in
            variant.OptionCombination.Selections)
        {
            var selectionRecord =
                record.OptionSelections
                    .SingleOrDefault(
                        persisted =>
                            persisted.OptionId ==
                            selection.OptionId.Value);

            if (selectionRecord is null)
            {
                record.OptionSelections.Add(
                    new ProductVariantOptionRecord
                    {
                        ProductId =
                            productId.Value,

                        ProductVariantId =
                            variant.Id.Value,

                        OptionId =
                            selection.OptionId.Value,

                        Value =
                            selection.Value.Value
                    });

                continue;
            }

            selectionRecord.Value =
                selection.Value.Value;
        }
    }

    private static T GetRequiredValue<T>(
        Result<T> result,
        string typeName)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException(
                $"The persisted value could not be rehydrated as {typeName}. Domain error: {result.Error?.Code}.");
        }

        return result.Value;
    }
}
