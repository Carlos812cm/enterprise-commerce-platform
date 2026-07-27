using Catalog.Application.Abstractions.Queries;
using Catalog.Application.Products.GetProductById;
using Dapper;
using Npgsql;

namespace Catalog.Infrastructure.Queries;

internal sealed class DapperProductDetailsReader(
    NpgsqlDataSource dataSource)
    : IProductDetailsReader
{
    private const string QueryText =
        """
        SELECT
            p.id AS "ProductId",
            p.name AS "Name",
            p.slug AS "Slug",
            p.description AS "Description",
            lower(p.status) AS "Status",
            p.version AS "Version",
            p.published_at_utc AS "PublishedAtUtc",
            p.discontinued_at_utc AS "DiscontinuedAtUtc"
        FROM catalog.products AS p
        WHERE p.id = @ProductId;

        SELECT
            po.id AS "OptionId",
            po.name AS "Name",
            po.display_order AS "DisplayOrder"
        FROM catalog.product_options AS po
        WHERE po.product_id = @ProductId
        ORDER BY
            po.display_order,
            po.id;

        SELECT
            pv.id AS "VariantId",
            pv.sku AS "Sku",
            lower(pv.status) AS "Status",
            pv.activated_at_utc AS "ActivatedAtUtc",
            pv.discontinued_at_utc AS "DiscontinuedAtUtc"
        FROM catalog.product_variants AS pv
        WHERE pv.product_id = @ProductId
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
        INNER JOIN catalog.product_options AS po
            ON po.product_id = pvo.product_id
            AND po.id = pvo.option_id
        WHERE pvo.product_id = @ProductId
        ORDER BY
            pvo.product_variant_id,
            po.display_order,
            pvo.option_id;
        """;

    public async Task<
        AdminProductDetailsReadModel?> GetByIdAsync(
            Guid productId,
            CancellationToken cancellationToken)
    {
        if (productId == Guid.Empty)
        {
            throw new ArgumentException(
                "A product identifier cannot be empty.",
                nameof(productId));
        }

        await using var connection =
            await dataSource
                .OpenConnectionAsync(
                    cancellationToken)
                .ConfigureAwait(false);

        var command = new CommandDefinition(
            QueryText,
            new
            {
                ProductId = productId
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
                                    new AdminProductVariantSelectionReadModel(
                                        row.OptionId,
                                        row.OptionName,
                                        row.DisplayOrder,
                                        row.Value))
                            .ToArray());

        var options =
            optionRows
                .Select(
                    static row =>
                        new AdminProductOptionReadModel(
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
                                AdminProductVariantSelectionReadModel>();

                    return new AdminProductVariantReadModel(
                        row.VariantId,
                        row.Sku,
                        row.Status,
                        row.ActivatedAtUtc,
                        row.DiscontinuedAtUtc,
                        selections);
                })
                .ToArray();

        return new AdminProductDetailsReadModel(
            productRow.ProductId,
            productRow.Name,
            productRow.Slug,
            productRow.Description,
            productRow.Status,
            productRow.Version,
            productRow.PublishedAtUtc,
            productRow.DiscontinuedAtUtc,
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

        public string Status { get; set; } =
            string.Empty;

        public long Version { get; set; }

        public DateTimeOffset? PublishedAtUtc { get; set; }

        public DateTimeOffset? DiscontinuedAtUtc { get; set; }
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

        public string Status { get; set; } =
            string.Empty;

        public DateTimeOffset? ActivatedAtUtc { get; set; }

        public DateTimeOffset? DiscontinuedAtUtc { get; set; }
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
