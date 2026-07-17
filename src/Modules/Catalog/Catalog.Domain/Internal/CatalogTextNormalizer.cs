using System.Text;

namespace Catalog.Domain.Internal;

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

    public static string CreateComparisonKey(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return CollapseWhitespace(value)
            .Normalize(NormalizationForm.FormC)
            .ToUpperInvariant();
    }
}
