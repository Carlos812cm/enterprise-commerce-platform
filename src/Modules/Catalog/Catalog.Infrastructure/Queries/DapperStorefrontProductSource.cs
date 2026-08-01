using Catalog.Application.Abstractions.Queries;
using Catalog.Application.Products.GetPublishedProductBySlug;
using Catalog.Domain.Products;
using Dapper;
using Npgsql;

namespace Catalog.Infrastructure.Queries;

internal sealed class DapperStorefrontProductSource(
    NpgsqlDataSource dataSource)
    : IStorefrontProductSource
{
    private const string QueryText =
        """
        SELECT
            p.id AS "ProductId",
            p.name AS "Name",
            p.slug AS "Slug",
            p.description AS "Description",
            p.version AS "Version"
        FROM catalog.products AS p
        WHERE p.slug = @Slug
          AND p.status = 'Published';

        SELECT
            po.id AS "OptionId",
            po.name AS "Name",
            po.display_order AS "DisplayOrder"
        FROM catalog.product_options AS po
        INNER JOIN catalog.products AS p
            ON p.id = po.product_id
        WHERE p.slug = @Slug
          AND p.status = 'Published'
        ORDER BY
            po.display_order,
            po.id;

        SELECT
            pv.id AS "VariantId",
            pv.sku AS "Sku"
        FROM catalog.product_variants AS pv
        INNER JOIN catalog.products AS p
            ON p.id = pv.product_id
        WHERE p.slug = @Slug
          AND p.status = 'Published'
          AND pv.status = 'Active'
        ORDER BY
            pv.sku,
            pv.id;

        SELECT
            pvo.product_variant_id AS "VariantId",
            pvo.option_id AS "OptionId",
            po.name AS "OptionName",
            po.display_order AS "DisplayOrder",
            pvo.value AS "Value"
        FROM catalog.product_variant_options AS pvo
        INNER JOIN catalog.product_variants AS pv
            ON pv.product_id = pvo.product_id
            AND pv.id = pvo.product_variant_id
        INNER JOIN catalog.product_options AS po
            ON po.product_id = pvo.product_id
            AND po.id = pvo.option_id
        INNER JOIN catalog.products AS p
            ON p.id = pvo.product_id
        WHERE p.slug = @Slug
          AND p.status = 'Published'
          AND pv.status = 'Active'
        ORDER BY
            pvo.product_variant_id,
            po.display_order,
            pvo.option_id;
        """;

    public async Task<
        PublishedProductDetailsReadModel?>
        GetBySlugAsync(
            ProductSlug slug,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(slug);

        await using var connection =
            await dataSource
                .OpenConnectionAsync(
                    cancellationToken)
                .ConfigureAwait(false);

        var command = new CommandDefinition(
            QueryText,
            new
            {
                Slug = slug.Value
            },
            cancellationToken:
                cancellationToken);

        using var resultSets =
            await connection
                .QueryMultipleAsync(command)
                .ConfigureAwait(false);

        var productRows =
            (
                await resultSets
                    .ReadAsync<ProductRow>()
                    .ConfigureAwait(false)
            )
            .ToArray();

        var optionRows =
            (
                await resultSets
                    .ReadAsync<ProductOptionRow>()
                    .ConfigureAwait(false)
            )
            .ToArray();

        var variantRows =
            (
                await resultSets
                    .ReadAsync<ProductVariantRow>()
                    .ConfigureAwait(false)
            )
            .ToArray();

        var selectionRows =
            (
                await resultSets
                    .ReadAsync<ProductVariantSelectionRow>()
                    .ConfigureAwait(false)
            )
            .ToArray();

        var productRow =
            productRows.SingleOrDefault();

        if (productRow is null)
        {
            return null;
        }

        var selectionsByVariant =
            selectionRows
                .GroupBy(
                    static row =>
                        row.VariantId)
                .ToDictionary(
                    static group =>
                        group.Key,
                    static group =>
                        group
                            .Select(
                                static row =>
                                    new PublishedProductVariantSelectionReadModel(
                                        row.OptionId,
                                        row.OptionName,
                                        row.DisplayOrder,
                                        row.Value))
                            .ToArray());

        var options =
            optionRows
                .Select(
                    static row =>
                        new PublishedProductOptionReadModel(
                            row.OptionId,
                            row.Name,
                            row.DisplayOrder))
                .ToArray();

        var variants =
            variantRows
                .Select(row =>
                {
                    var selections =
                        selectionsByVariant.TryGetValue(
                            row.VariantId,
                            out var mappedSelections)
                            ? mappedSelections
                            : Array.Empty<
                                PublishedProductVariantSelectionReadModel>();

                    return new PublishedProductVariantReadModel(
                        row.VariantId,
                        row.Sku,
                        selections);
                })
                .ToArray();

        return new PublishedProductDetailsReadModel(
            productRow.ProductId,
            productRow.Name,
            productRow.Slug,
            productRow.Description,
            productRow.Version,
            options,
            variants);
    }

    private sealed class ProductRow
    {
        public Guid ProductId { get; set; }

        public string Name { get; set; } =
            string.Empty;

        public string Slug { get; set; } =
            string.Empty;

        public string Description { get; set; } =
            string.Empty;

        public long Version { get; set; }
    }

    private sealed class ProductOptionRow
    {
        public Guid OptionId { get; set; }

        public string Name { get; set; } =
            string.Empty;

        public int DisplayOrder { get; set; }
    }

    private sealed class ProductVariantRow
    {
        public Guid VariantId { get; set; }

        public string Sku { get; set; } =
            string.Empty;
    }

    private sealed class ProductVariantSelectionRow
    {
        public Guid VariantId { get; set; }

        public Guid OptionId { get; set; }

        public string OptionName { get; set; } =
            string.Empty;

        public int DisplayOrder { get; set; }

        public string Value { get; set; } =
            string.Empty;
    }
}
