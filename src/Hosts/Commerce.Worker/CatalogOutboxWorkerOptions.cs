namespace Commerce.Worker;

internal sealed class CatalogOutboxWorkerOptions
{
    public const string SectionName =
        "CatalogOutbox";

    public int BatchSize { get; set; } =
        16;

    public TimeSpan LeaseDuration { get; set; } =
        TimeSpan.FromMinutes(1);

    public TimeSpan IdleDelay { get; set; } =
        TimeSpan.FromSeconds(1);
}
