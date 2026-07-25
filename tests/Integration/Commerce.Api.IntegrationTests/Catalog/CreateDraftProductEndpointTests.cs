using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Catalog.Application.Abstractions.Persistence;
using Catalog.Domain.Products;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Commerce.Api.IntegrationTests.Catalog;

public sealed class CreateDraftProductEndpointTests :
    IClassFixture<CommerceApiFixture>
{
    private readonly CommerceApiFixture _fixture;

    public CreateDraftProductEndpointTests(
        CommerceApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task EndpointRejectsAnonymousCaller()
    {
        using var client =
            _fixture.CreateClient(
                authenticated: false,
                authorized: false);

        var response = await client.PostAsJsonAsync(
            "/api/catalog/products",
            CreateRequest(
                CreateSlug()),
            TestContext.Current
                .CancellationToken);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    [Fact]
    public async Task EndpointRejectsCallerWithoutPermission()
    {
        using var client =
            _fixture.CreateClient(
                authenticated: true,
                authorized: false);

        var response = await client.PostAsJsonAsync(
            "/api/catalog/products",
            CreateRequest(
                CreateSlug()),
            TestContext.Current
                .CancellationToken);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            response.StatusCode);
    }

    [Fact]
    public async Task EndpointCreatesAndPersistsDraftProduct()
    {
        using var client =
            _fixture.CreateClient(
                authenticated: true,
                authorized: true);

        var slug = CreateSlug();

        var response = await client.PostAsJsonAsync(
            "/api/catalog/products",
            CreateRequest(slug),
            TestContext.Current
                .CancellationToken);

        Assert.Equal(
            HttpStatusCode.Created,
            response.StatusCode);

        var body =
            await response.Content
                .ReadFromJsonAsync<
                    CreateProductResponse>(
                    cancellationToken:
                        TestContext.Current
                            .CancellationToken);

        Assert.NotNull(body);
        Assert.NotEqual(
            Guid.Empty,
            body.ProductId);

        Assert.Equal(
            "draft",
            body.Status);

        Assert.Equal(
            $"/api/catalog/products/{body.ProductId:D}",
            response.Headers.Location?.ToString());

        await using var scope =
            _fixture.Services
                .CreateAsyncScope();

        var repository =
            scope.ServiceProvider
                .GetRequiredService<
                    IProductRepository>();

        var product =
            await repository.GetByIdAsync(
                ProductId.Create(
                    body.ProductId),
                TestContext.Current
                    .CancellationToken);

        Assert.NotNull(product);
        Assert.Equal(
            slug,
            product.Slug.Value);

        Assert.Equal(
            ProductStatus.Draft,
            product.Status);
    }

    [Fact]
    public async Task InvalidSlugReturnsProblemDetails()
    {
        using var client =
            _fixture.CreateClient(
                authenticated: true,
                authorized: true);

        var response = await client.PostAsJsonAsync(
            "/api/catalog/products",
            CreateRequest("Invalid Slug"),
            TestContext.Current
                .CancellationToken);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);

        var problem =
            await response.Content
                .ReadFromJsonAsync<ProblemDetails>(
                    cancellationToken:
                        TestContext.Current
                            .CancellationToken);

        Assert.NotNull(problem);

        Assert.Equal(
            "Catalog.Product.InvalidSlug",
            GetExtensionString(
                problem,
                "code"));

        Assert.False(
            string.IsNullOrWhiteSpace(
                GetExtensionString(
                    problem,
                    "traceId")));
    }

    [Fact]
    public async Task DuplicateSlugReturnsConflict()
    {
        using var client =
            _fixture.CreateClient(
                authenticated: true,
                authorized: true);

        var slug = CreateSlug();
        var request = CreateRequest(slug);

        var firstResponse =
            await client.PostAsJsonAsync(
                "/api/catalog/products",
                request,
                TestContext.Current
                    .CancellationToken);

        Assert.Equal(
            HttpStatusCode.Created,
            firstResponse.StatusCode);

        var secondResponse =
            await client.PostAsJsonAsync(
                "/api/catalog/products",
                request,
                TestContext.Current
                    .CancellationToken);

        Assert.Equal(
            HttpStatusCode.Conflict,
            secondResponse.StatusCode);

        var problem =
            await secondResponse.Content
                .ReadFromJsonAsync<ProblemDetails>(
                    cancellationToken:
                        TestContext.Current
                            .CancellationToken);

        Assert.NotNull(problem);

        Assert.Equal(
            "Catalog.Product.SlugAlreadyExists",
            GetExtensionString(
                problem,
                "code"));
    }

    private static object CreateRequest(
        string slug)
    {
        return new
        {
            name = "Enterprise Keyboard",
            slug,
            description =
                "Premium enterprise keyboard."
        };
    }

    private static string CreateSlug()
    {
        return string.Concat(
            "http-product-",
            Guid.CreateVersion7()
                .ToString("N"));
    }

    private static string? GetExtensionString(
        ProblemDetails problem,
        string key)
    {
        if (!problem.Extensions.TryGetValue(
                key,
                out var value))
        {
            return null;
        }

        return value switch
        {
            JsonElement element =>
                element.GetString(),

            string text =>
                text,

            _ =>
                value?.ToString()
        };
    }

    private sealed record CreateProductResponse(
        Guid ProductId,
        string Status);
}
