using Catalog.Domain.Products;

namespace Catalog.Infrastructure.Persistence.Records;

internal sealed class ProductVariantRecord
{
    public Guid Id { get; set; }

    public Guid ProductId { get; set; }

    public string Sku { get; set; } =
        string.Empty;

    public ProductVariantStatus Status { get; set; }

    public string OptionCombinationKey { get; set; } =
        string.Empty;

    public DateTimeOffset? ActivatedAtUtc { get; set; }

    public DateTimeOffset? DiscontinuedAtUtc { get; set; }

    public List<ProductVariantOptionRecord> OptionSelections { get; } =
        [];
}
