namespace Catalog.Infrastructure.Persistence.Records;

internal sealed class ProductVariantOptionRecord
{
    public Guid ProductId { get; set; }

    public Guid ProductVariantId { get; set; }

    public Guid OptionId { get; set; }

    public string Value { get; set; } =
        string.Empty;
}
