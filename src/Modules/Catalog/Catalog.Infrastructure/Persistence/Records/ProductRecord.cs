using Catalog.Domain.Products;

namespace Catalog.Infrastructure.Persistence.Records;

internal sealed class ProductRecord
{
    public Guid Id { get; set; }

    public string Name { get; set; } =
        string.Empty;

    public string Slug { get; set; } =
        string.Empty;

    public string Description { get; set; } =
        string.Empty;

    public ProductStatus Status { get; set; }

    public DateTimeOffset? PublishedAtUtc { get; set; }

    public DateTimeOffset? DiscontinuedAtUtc { get; set; }

    public long Version { get; set; }

    public List<ProductOptionRecord> OptionDefinitions { get; } =
        [];

    public List<ProductVariantRecord> Variants { get; } =
        [];
}
