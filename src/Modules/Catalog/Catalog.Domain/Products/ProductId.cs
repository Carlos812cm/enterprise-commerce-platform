using System.Globalization;

namespace Catalog.Domain.Products;

public readonly record struct ProductId
{
    private ProductId(Guid value)
    {
        Value = value;
    }

    public Guid Value { get; }

    public bool IsEmpty => Value == Guid.Empty;

    public static ProductId Generate()
    {
        return new ProductId(Guid.CreateVersion7());
    }

    public static ProductId Create(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(
                "A product identifier cannot be empty.",
                nameof(value));
        }

        return new ProductId(value);
    }

    public override string ToString()
    {
        return Value.ToString(
            "D",
            CultureInfo.InvariantCulture);
    }
}
