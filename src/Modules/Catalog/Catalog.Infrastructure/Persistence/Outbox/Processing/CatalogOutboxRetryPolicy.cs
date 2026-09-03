namespace Catalog.Infrastructure.Persistence.Outbox.Processing;

internal static class CatalogOutboxRetryPolicy
{
    private const double InitialDelaySeconds = 5;
    private const double MaximumDelaySeconds = 300;

    public const int MaximumAttempts = 5;

    public static TimeSpan GetDelay(
        int failedAttemptNumber)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            failedAttemptNumber);

        var exponent =
            Math.Min(
                failedAttemptNumber - 1,
                30);

        var delaySeconds =
            Math.Min(
                InitialDelaySeconds *
                Math.Pow(
                    2,
                    exponent),
                MaximumDelaySeconds);

        return TimeSpan.FromSeconds(
            delaySeconds);
    }
}
