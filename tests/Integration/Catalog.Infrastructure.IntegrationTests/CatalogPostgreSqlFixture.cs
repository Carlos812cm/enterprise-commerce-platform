using Catalog.Application;
using Catalog.Infrastructure;
using Catalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Catalog.Infrastructure.IntegrationTests;

public sealed class CatalogPostgreSqlFixture :
    IAsyncLifetime
{
    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder("postgres:18.4")
            .WithDatabase("commerce")
            .WithUsername("commerce")
            .WithPassword("commerce_test_password")
            .Build();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        await using var serviceProvider =
            CreateServiceProvider(
                TimeProvider.System);

        await using var scope =
            serviceProvider.CreateAsyncScope();

        var dbContext =
            scope.ServiceProvider
                .GetRequiredService<CatalogDbContext>();

        await dbContext.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    public ServiceProvider CreateServiceProvider(
    TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        var connectionString =
            _container.GetConnectionString();

        var services = new ServiceCollection();

        services.AddLogging();

        services.AddSingleton<TimeProvider>(
            timeProvider);

        services.AddSingleton(
            static serviceProvider =>
            {
                var connection =
                    serviceProvider
                        .GetRequiredService<
                            CatalogTestConnection>();

                return NpgsqlDataSource.Create(
                    connection.Value);
            });

        services.AddSingleton(
            new CatalogTestConnection(
                connectionString));

        services.AddCatalogApplication();
        services.AddCatalogInfrastructure();

        return services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });
    }

    private sealed record CatalogTestConnection(
        string Value);

}
