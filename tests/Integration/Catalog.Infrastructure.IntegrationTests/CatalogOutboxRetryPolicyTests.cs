using Catalog.Infrastructure.Persistence.Outbox.Processing;
using Xunit;

namespace Catalog.Infrastructure.IntegrationTests;

public sealed class CatalogOutboxRetryPolicyTests
{
    [Fact]
    public void UsesDeterministicExponentialBackoff()
    {
        Assert.Equal(
            TimeSpan.FromSeconds(5),
            CatalogOutboxRetryPolicy.GetDelay(1));

        Assert.Equal(
            TimeSpan.FromSeconds(10),
            CatalogOutboxRetryPolicy.GetDelay(2));

        Assert.Equal(
            TimeSpan.FromSeconds(20),
            CatalogOutboxRetryPolicy.GetDelay(3));

        Assert.Equal(
            TimeSpan.FromSeconds(40),
            CatalogOutboxRetryPolicy.GetDelay(4));

        Assert.Equal(
            TimeSpan.FromMinutes(5),
            CatalogOutboxRetryPolicy.GetDelay(100));
    }

    [Fact]
    public void RejectsInvalidAttemptNumber()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                CatalogOutboxRetryPolicy.GetDelay(0));

        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                CatalogOutboxRetryPolicy.GetDelay(-1));
    }
}
