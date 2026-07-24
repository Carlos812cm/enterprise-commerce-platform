namespace Catalog.Infrastructure.Persistence.Records;

internal sealed class ProductOptionRecord
{
    public Guid Id { get; set; }

    public Guid ProductId { get; set; }

    public string Name { get; set; } =
        string.Empty;

    public string NameKey { get; set; } =
        string.Empty;

    public int DisplayOrder { get; set; }
}
