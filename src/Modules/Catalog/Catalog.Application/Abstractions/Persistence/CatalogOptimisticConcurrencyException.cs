namespace Catalog.Application.Abstractions.Persistence;

public sealed class CatalogOptimisticConcurrencyException :
    Exception
{
    private const string DefaultMessage =
        "The Catalog aggregate was modified by another operation.";

    public CatalogOptimisticConcurrencyException()
        : base(DefaultMessage)
    {
    }

    public CatalogOptimisticConcurrencyException(
        string message)
        : base(message)
    {
    }

    public CatalogOptimisticConcurrencyException(
        string message,
        Exception innerException)
        : base(
            message,
            innerException)
    {
    }
}
