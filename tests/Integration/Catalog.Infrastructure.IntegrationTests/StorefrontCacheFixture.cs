using Testcontainers.Redis;
using Xunit;

namespace Catalog.Infrastructure.IntegrationTests;

public sealed class StorefrontCacheFixture :
    IAsyncLifetime
{
    private readonly RedisContainer _redis =
        new RedisBuilder(
            "redis:8.4.4")
            .Build();

    public string ConnectionString =>
        _redis.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _redis.StartAsync(
            TestContext.Current
                .CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _redis.DisposeAsync();
    }
}
