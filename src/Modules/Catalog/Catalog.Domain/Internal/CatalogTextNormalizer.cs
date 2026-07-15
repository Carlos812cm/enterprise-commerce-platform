using System.Text; // esto es para poder usar StringBuilder, que es una clase que permite construir cadenas de texto de manera eficiente, especialmente cuando se realizan muchas concatenaciones.

namespace Catalog.Domain.Internal; // esto indica que la clase CatalogTextNormalizer pertenece al espacio de nombres Catalog.Domain.Internal, lo que ayuda a organizar el código y evitar conflictos de nombres.

internal static class CatalogTextNormalizer
{
    public static string CollapseWhitespace(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var builder = new StringBuilder(value.Length);
        var separatorPending = false;

        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character))
            {
                separatorPending = builder.Length > 0;
                continue;
            }

            if (separatorPending)
            {
                builder.Append(' ');
                separatorPending = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}
