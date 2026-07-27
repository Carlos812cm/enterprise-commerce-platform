using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Commerce.Api.IntegrationTests.Catalog;

public sealed class GetProductByIdEndpointTests :
    IClassFixture<CommerceApiFixture>
{
    private const string ProductsPath =
        "/api/catalog/products";

    private const string ProductPathTemplate =
        "/api/catalog/products/{productId}";

    private readonly CommerceApiFixture _fixture;

    public GetProductByIdEndpointTests(
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

        var response = await client.GetAsync(
            GetProductPath(
                Guid.CreateVersion7()),
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

        var response = await client.GetAsync(
            GetProductPath(
                Guid.CreateVersion7()),
            TestContext.Current
                .CancellationToken);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            response.StatusCode);
    }

    [Fact]
    public async Task CreatedProductCanBeReadFromLocation()
    {
        using var client =
            _fixture.CreateClient(
                authenticated: true,
                authorized: true);

        var createResponse =
            await client.PostAsJsonAsync(
                ProductsPath,
                CreateRequest(),
                TestContext.Current
                    .CancellationToken);

        Assert.Equal(
            HttpStatusCode.Created,
            createResponse.StatusCode);

        var createdProduct =
            await createResponse.Content
                .ReadFromJsonAsync<
                    CreateProductResponse>(
                    cancellationToken:
                        TestContext.Current
                            .CancellationToken);

        Assert.NotNull(createdProduct);
        Assert.NotNull(
            createResponse.Headers.Location);

        var getResponse = await client.GetAsync(
            createResponse.Headers.Location,
            TestContext.Current
                .CancellationToken);

        Assert.Equal(
            HttpStatusCode.OK,
            getResponse.StatusCode);

        var product =
            await getResponse.Content
                .ReadFromJsonAsync<
                    GetProductResponse>(
                    cancellationToken:
                        TestContext.Current
                            .CancellationToken);

        Assert.NotNull(product);

        Assert.Equal(
            createdProduct.ProductId,
            product.ProductId);

        Assert.Equal(
            1,
            product.Version);

        Assert.Equal(
            "draft",
            product.Status);

        var cacheControl =
            getResponse.Headers
                .GetValues("Cache-Control")
                .Single();

        Assert.Contains(
            "no-store",
            cacheControl,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmptyProductIdReturnsProblemDetails()
    {
        using var client =
            _fixture.CreateClient(
                authenticated: true,
                authorized: true);

        var response = await client.GetAsync(
            GetProductPath(Guid.Empty),
            TestContext.Current
                .CancellationToken);

        await AssertProblemAsync(
            response,
            HttpStatusCode.BadRequest,
            "Catalog.Product.InvalidId");
    }

    [Fact]
    public async Task MissingProductReturnsNotFoundProblemDetails()
    {
        using var client =
            _fixture.CreateClient(
                authenticated: true,
                authorized: true);

        var response = await client.GetAsync(
            GetProductPath(
                Guid.CreateVersion7()),
            TestContext.Current
                .CancellationToken);

        await AssertProblemAsync(
            response,
            HttpStatusCode.NotFound,
            "Catalog.Product.NotFound");
    }

    [Fact]
    public async Task OpenApiContainsGetProductByIdOperation()
    {
        using var client =
            _fixture.CreateClient(
                authenticated: false,
                authorized: false);

        var response = await client.GetAsync(
            "/openapi/v1.json",
            TestContext.Current
                .CancellationToken);

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        await using var contentStream =
            await response.Content.ReadAsStreamAsync(
                TestContext.Current
                    .CancellationToken);

        using var document =
            await JsonDocument.ParseAsync(
                contentStream,
                cancellationToken:
                    TestContext.Current
                        .CancellationToken);

        var paths =
            document.RootElement
                .GetProperty("paths");

        Assert.True(
            paths.TryGetProperty(
                ProductPathTemplate,
                out var productPath));

        Assert.True(
            productPath.TryGetProperty(
                "get",
                out _));
    }

    private static object CreateRequest()
    {
        return new
        {
            name = "Queried Enterprise Keyboard",
            slug = string.Concat(
                "queried-product-",
                Guid.CreateVersion7()
                    .ToString("N")),
            description =
                "Product created before a GET request."
        };
    }

    private static string GetProductPath(
        Guid productId)
    {
        return string.Concat(
            ProductsPath,
            "/",
            productId.ToString("D"));
    }

    private static async Task AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatusCode,
        string expectedCode)
    {
        Assert.Equal(
            expectedStatusCode,
            response.StatusCode);

        var problem =
            await response.Content
                .ReadFromJsonAsync<ProblemDetails>(
                    cancellationToken:
                        TestContext.Current
                            .CancellationToken);

        Assert.NotNull(problem);

        Assert.Equal(
            expectedCode,
            GetExtensionString(
                problem,
                "code"));
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

    private sealed record GetProductResponse(
        Guid ProductId,
        string Status,
        long Version);
}
