using System.Globalization;

namespace Catalog.Domain.Products;

public readonly record struct ProductOptionId
{
    private ProductOptionId(Guid value)
    {
        Value = value;
    }

    public Guid Value { get; }

    public bool IsEmpty => Value == Guid.Empty;

    public static ProductOptionId Generate()
    {
        return new ProductOptionId(Guid.CreateVersion7());
    }

    public static ProductOptionId Create(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(
                "A product option identifier cannot be empty.",
                nameof(value));
        }

        return new ProductOptionId(value);
    }

    public override string ToString()
    {
        return Value.ToString(
            "D",
            CultureInfo.InvariantCulture);
    }
}
