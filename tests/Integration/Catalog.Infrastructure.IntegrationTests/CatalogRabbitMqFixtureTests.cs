using Xunit;

namespace Catalog.Infrastructure.IntegrationTests;

public sealed class CatalogRabbitMqFixtureTests :
    IClassFixture<CatalogRabbitMqFixture>
{
    private readonly CatalogRabbitMqFixture _fixture;

    public CatalogRabbitMqFixtureTests(
        CatalogRabbitMqFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task
        RabbitMqContainerAcceptsClientConnection()
    {
        await using var connection =
            await _fixture.CreateConnectionAsync(
                TestContext.Current.CancellationToken);

        Assert.True(
            connection.IsOpen);
    }
}
