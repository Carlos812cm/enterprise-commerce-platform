using Catalog.Infrastructure.Persistence;
using Commerce.Api.IntegrationTests.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;
using CatalogAuthorization =
    global::Catalog.Api.Authorization.CatalogAuthorization;

namespace Commerce.Api.IntegrationTests;

public sealed class CommerceApiFixture :
    IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres =
        new PostgreSqlBuilder("postgres:18.4")
            .WithDatabase("commerce")
            .WithUsername("commerce")
            .WithPassword(
                "commerce_http_test_password")
            .Build();

    private NpgsqlDataSource? _dataSource;
    private TestCommerceApiFactory? _factory;

    public IServiceProvider Services =>
        GetFactory().Services;

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync(
            TestContext.Current
                .CancellationToken);

        _dataSource =
            NpgsqlDataSource.Create(
                _postgres.GetConnectionString());

        _factory =
            new TestCommerceApiFactory(
                _dataSource);

        await using var scope =
            Services.CreateAsyncScope();

        var dbContext =
            scope.ServiceProvider
                .GetRequiredService<
                    CatalogDbContext>();

        await dbContext.Database.MigrateAsync(
            TestContext.Current
                .CancellationToken);
    }

    public HttpClient CreateClient(
        bool authenticated,
        bool authorized)
    {
        var client =
            GetFactory().CreateClient(
                new WebApplicationFactoryClientOptions
                {
                    AllowAutoRedirect = false
                });

        if (authenticated)
        {
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers
                    .AuthenticationHeaderValue(
                        TestAuthenticationHandler
                            .SchemeName);
        }

        if (authorized)
        {
            client.DefaultRequestHeaders.Add(
                TestAuthenticationHandler
                    .PermissionHeader,
                CatalogAuthorization
                    .ProductsWritePermission);
        }

        return client;
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        if (_dataSource is not null)
        {
            await _dataSource.DisposeAsync();
        }

        await _postgres.DisposeAsync();
    }

    private TestCommerceApiFactory GetFactory()
    {
        return _factory ??
            throw new InvalidOperationException(
                "The API test fixture has not been initialized.");
    }

    private sealed class TestCommerceApiFactory(
        NpgsqlDataSource dataSource)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(
            IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");

            builder.UseSetting(
                "ConnectionStrings:Postgres",
                "Host=unused");

            builder.UseSetting(
                "ConnectionStrings:Redis",
                "localhost:1,abortConnect=false");

            builder.UseSetting(
                "ConnectionStrings:RabbitMq",
                "amqp://guest:guest@localhost:1/");

            builder.UseSetting(
                "Authentication:MetadataAddress",
                "http://identity.invalid/.well-known/openid-configuration");

            builder.UseSetting(
                "Authentication:ValidIssuer",
                "http://identity.invalid");

            builder.UseSetting(
                "Authentication:Audience",
                "commerce-api");

            builder.UseSetting(
                "Authentication:RequireHttpsMetadata",
                "false");

            builder.ConfigureTestServices(
                services =>
                {
                    services.RemoveAll<
                        NpgsqlDataSource>();

                    services.AddSingleton(
                        dataSource);

                    services
                        .AddAuthentication(options =>
                        {
                            options.DefaultAuthenticateScheme =
                                TestAuthenticationHandler
                                    .SchemeName;

                            options.DefaultChallengeScheme =
                                TestAuthenticationHandler
                                    .SchemeName;

                            options.DefaultForbidScheme =
                                TestAuthenticationHandler
                                    .SchemeName;
                        })
                        .AddScheme<
                            AuthenticationSchemeOptions,
                            TestAuthenticationHandler>(
                            TestAuthenticationHandler
                                .SchemeName,
                            static _ =>
                            {
                            });
                });
        }
    }
}
