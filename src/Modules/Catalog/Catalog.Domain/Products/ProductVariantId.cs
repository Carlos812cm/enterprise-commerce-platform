using System.Globalization;

namespace Catalog.Domain.Products;

public readonly record struct ProductVariantId
{
    private ProductVariantId(Guid value)
    {
        Value = value;
    }

    public Guid Value { get; }

    public bool IsEmpty => Value == Guid.Empty;

#pragma warning disable CA1711 // Los identificadores no deben tener un sufijo incorrecto
    public static ProductVariantId CreateNew()
#pragma warning restore CA1711 // Los identificadores no deben tener un sufijo incorrecto
    {
        return new ProductVariantId(Guid.CreateVersion7());
    }

    public static ProductVariantId Create(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(
                "A product variant identifier cannot be empty.",
                nameof(value));
        }

        return new ProductVariantId(value);
    }

    public override string ToString()
    {
        return Value.ToString(
            "D",
            CultureInfo.InvariantCulture);
    }
}
