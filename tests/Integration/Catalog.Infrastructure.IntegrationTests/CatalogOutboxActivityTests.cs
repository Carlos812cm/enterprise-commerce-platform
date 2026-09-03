using System.Diagnostics;
using Catalog.Infrastructure.Persistence.Outbox.Processing;
using Xunit;

namespace Catalog.Infrastructure.IntegrationTests;

public sealed class CatalogOutboxActivityTests
{
    private const string TraceParent =
        "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";

    private const string TraceState =
        "vendor=value";

    [Fact]
    public void
        StoredContextIsRestoredWhileInvalidAndMissingContextsIgnoreAmbientActivity()
    {
        using var listener =
            new ActivityListener
            {
                ShouldListenTo =
                    source =>
                        source.Name ==
                        CatalogOutboxActivity
                            .ActivitySourceName,

                Sample =
                    static (
                        ref ActivityCreationOptions<ActivityContext> _) =>
                            ActivitySamplingResult
                                .AllDataAndRecorded
            };

        ActivitySource.AddActivityListener(
            listener);

        var originalActivity =
            Activity.Current;

        using (
            var tracedScope =
                CatalogOutboxActivity.Start(
                    CreateMessage(
                        TraceParent,
                        TraceState)))
        {
            var tracedActivity =
                tracedScope.Activity;

            Assert.NotNull(
                tracedActivity);

            Assert.Same(
                tracedActivity,
                Activity.Current);

            Assert.Equal(
                CatalogOutboxActivity
                    .ProcessActivityName,
                tracedActivity.OperationName);

            Assert.Equal(
                ActivityKind.Internal,
                tracedActivity.Kind);

            Assert.Equal(
                "4bf92f3577b34da6a3ce929d0e0e4736",
                tracedActivity.TraceId.ToString());

            Assert.Equal(
                "00f067aa0ba902b7",
                tracedActivity.ParentSpanId.ToString());

            Assert.Equal(
                TraceState,
                tracedActivity.TraceStateString);

            using (
                var invalidScope =
                    CatalogOutboxActivity.Start(
                        CreateMessage(
                            "not-a-valid-trace-parent",
                            TraceState)))
            {
                var invalidActivity =
                    invalidScope.Activity;

                Assert.NotNull(
                    invalidActivity);

                Assert.Same(
                    invalidActivity,
                    Activity.Current);

                Assert.Equal(
                    default,
                    invalidActivity.ParentSpanId);

                Assert.NotEqual(
                    tracedActivity.TraceId,
                    invalidActivity.TraceId);
            }

            Assert.Same(
                tracedActivity,
                Activity.Current);

            using (
                var uncorrelatedScope =
                    CatalogOutboxActivity.Start(
                        CreateMessage(
                            traceParent: null,
                            traceState: null)))
            {
                var uncorrelatedActivity =
                    uncorrelatedScope.Activity;

                Assert.NotNull(
                    uncorrelatedActivity);

                Assert.Same(
                    uncorrelatedActivity,
                    Activity.Current);

                Assert.Equal(
                    default,
                    uncorrelatedActivity.ParentSpanId);

                Assert.NotEqual(
                    tracedActivity.TraceId,
                    uncorrelatedActivity.TraceId);
            }

            Assert.Same(
                tracedActivity,
                Activity.Current);
        }

        Assert.Same(
            originalActivity,
            Activity.Current);
    }

    private static ClaimedCatalogOutboxMessage
        CreateMessage(
            string? traceParent,
            string? traceState)
    {
        return new ClaimedCatalogOutboxMessage(
            Guid.CreateVersion7(),
            "catalog.test.v1",
            "{}",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            AttemptCount: 0,
            LeaseOwner:
                "worker-test:lease",
            LockedUntilUtc:
                DateTimeOffset.UtcNow
                    .AddMinutes(1),
            traceParent,
            traceState);
    }
}
