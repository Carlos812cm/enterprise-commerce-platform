using System.Diagnostics;

namespace Catalog.Infrastructure.Persistence.Outbox.Processing;

internal static class CatalogOutboxActivity
{
    internal const string ActivitySourceName =
        "Commerce.Catalog.Outbox";

    internal const string ProcessActivityName =
        "catalog.outbox.process";

    private static readonly ActivitySource ActivitySource =
        new(ActivitySourceName);

    public static CatalogOutboxActivityScope Start(
        ClaimedCatalogOutboxMessage message)
    {
        ArgumentNullException.ThrowIfNull(
            message);

        var previousActivity =
            Activity.Current;

        try
        {
            Activity.Current = null;

            Activity? activity;

            if (
                message.TraceParent is
                { Length: > 0 } traceParent &&
                ActivityContext.TryParse(
                    traceParent,
                    message.TraceState,
                    isRemote: true,
                    out var parentContext))
            {
                activity =
                    ActivitySource.StartActivity(
                        ProcessActivityName,
                        ActivityKind.Internal,
                        parentContext);
            }
            else
            {
                activity =
                    ActivitySource.StartActivity(
                        ProcessActivityName,
                        ActivityKind.Internal,
                        default(ActivityContext));
            }

            return new CatalogOutboxActivityScope(
                activity,
                previousActivity);
        }
        catch
        {
            Activity.Current =
                previousActivity;

            throw;
        }
    }
}

internal sealed class CatalogOutboxActivityScope :
    IDisposable
{
    private readonly Activity? _activity;

    private readonly Activity? _previousActivity;

    private bool _disposed;

    public CatalogOutboxActivityScope(
        Activity? activity,
        Activity? previousActivity)
    {
        _activity =
            activity;

        _previousActivity =
            previousActivity;
    }

    public Activity? Activity =>
        _activity;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _activity?.Dispose();
        }
        finally
        {
            Activity.Current =
                _previousActivity;
        }
    }
}
