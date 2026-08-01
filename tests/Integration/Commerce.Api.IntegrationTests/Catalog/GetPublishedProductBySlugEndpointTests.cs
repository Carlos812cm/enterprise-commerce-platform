using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Catalog.Application.Abstractions.Persistence;
using Catalog.Domain.Products;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Commerce.Api.IntegrationTests.Catalog;

public sealed class GetPublishedProductBySlugEndpointTests :
    IClassFixture<CommerceApiFixture>
{
    private const string StorefrontProductsPath =
        "/api/storefront/products";

    private const string ProductPathTemplate =
        "/api/storefront/products/{slug}";

    private static readonly DateTimeOffset CreatedAtUtc =
        new(
            2026,
            7,
            31,
            12,
            0,
            0,
            TimeSpan.Zero);

    private readonly CommerceApiFixture _fixture;

    public GetPublishedProductBySlugEndpointTests(
        CommerceApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AnonymousRequestReturnsPublishedProduct()
    {
        var slug = CreateSlug();
        var product = CreatePublishedProduct(slug);

        await PersistAsync(product);

        using var client =
            _fixture.CreateClient(
                authenticated: false,
                authorized: false);

        using var response = await client.GetAsync(
            GetProductPath(slug),
            TestContext.Current
                .CancellationToken);

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        var body =
            await response.Content
                .ReadFromJsonAsync<
                    PublishedProductResponse>(
                    cancellationToken:
                        TestContext.Current
                            .CancellationToken);

        Assert.NotNull(body);
        Assert.Equal(product.Id.Value, body.ProductId);
        Assert.Equal(slug, body.Slug);
        Assert.Equal("Storefront Product", body.Name);

        Assert.NotNull(response.Headers.ETag);

        var cacheControl =
            response.Headers.CacheControl;

        Assert.NotNull(cacheControl);
        Assert.True(cacheControl.Public);

        Assert.Equal(
            TimeSpan.FromSeconds(30),
            cacheControl.MaxAge);
    }

    [Fact]
    public async Task DraftProductReturnsNotFound()
    {
        var slug = CreateSlug();

        await PersistAsync(
            CreateDraftProduct(slug));

        using var client =
            _fixture.CreateClient(
                authenticated: false,
                authorized: false);

        using var response = await client.GetAsync(
            GetProductPath(slug),
            TestContext.Current
                .CancellationToken);

        await AssertProblemAsync(
            response,
            HttpStatusCode.NotFound,
            "Catalog.Storefront.ProductNotFound");
    }

    [Fact]
    public async Task DiscontinuedProductReturnsNotFound()
    {
        var slug = CreateSlug();
        var product = CreatePublishedProduct(slug);

        var discontinueResult = product.Discontinue(
            CreatedAtUtc.AddMinutes(3));

        Assert.True(
            discontinueResult.IsSuccess,
            discontinueResult.Error?.Code);

        await PersistAsync(product);

        using var client =
            _fixture.CreateClient(
                authenticated: false,
                authorized: false);

        using var response = await client.GetAsync(
            GetProductPath(slug),
            TestContext.Current
                .CancellationToken);

        await AssertProblemAsync(
            response,
            HttpStatusCode.NotFound,
            "Catalog.Storefront.ProductNotFound");
    }

    [Fact]
    public async Task PublishedProductReturnsOnlyActiveVariants()
    {
        var slug = CreateSlug();

        var testProduct =
            CreateProductWithDiscontinuedVariant(
                slug);

        await PersistAsync(testProduct.Product);

        using var client =
            _fixture.CreateClient(
                authenticated: false,
                authorized: false);

        using var response = await client.GetAsync(
            GetProductPath(slug),
            TestContext.Current
                .CancellationToken);

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        var body =
            await response.Content
                .ReadFromJsonAsync<
                    PublishedProductResponse>(
                    cancellationToken:
                        TestContext.Current
                            .CancellationToken);

        Assert.NotNull(body);

        var variant = Assert.Single(body.Variants);

        Assert.Equal(
            testProduct.ActiveSku,
            variant.Sku);

        Assert.DoesNotContain(
            body.Variants,
            candidate =>
                candidate.Sku ==
                testProduct.DiscontinuedSku);
    }

    [Fact]
    public async Task InvalidSlugReturnsBadRequest()
    {
        using var client =
            _fixture.CreateClient(
                authenticated: false,
                authorized: false);

        using var response = await client.GetAsync(
            string.Concat(
                StorefrontProductsPath,
                "/Invalid%20Slug"),
            TestContext.Current
                .CancellationToken);

        await AssertProblemAsync(
            response,
            HttpStatusCode.BadRequest,
            "Catalog.Product.InvalidSlug");
    }

    [Fact]
    public async Task MissingProductReturnsNotFound()
    {
        using var client =
            _fixture.CreateClient(
                authenticated: false,
                authorized: false);

        using var response = await client.GetAsync(
            GetProductPath(CreateSlug()),
            TestContext.Current
                .CancellationToken);

        await AssertProblemAsync(
            response,
            HttpStatusCode.NotFound,
            "Catalog.Storefront.ProductNotFound");
    }

    [Fact]
    public async Task CurrentEntityTagReturnsNotModifiedWithoutBody()
    {
        var slug = CreateSlug();

        await PersistAsync(
            CreatePublishedProduct(slug));

        using var client =
            _fixture.CreateClient(
                authenticated: false,
                authorized: false);

        using var firstResponse =
            await client.GetAsync(
                GetProductPath(slug),
                TestContext.Current
                    .CancellationToken);

        Assert.Equal(
            HttpStatusCode.OK,
            firstResponse.StatusCode);

        var entityTag =
            firstResponse.Headers.ETag;

        Assert.NotNull(entityTag);

        using var conditionalRequest =
            new HttpRequestMessage(
                HttpMethod.Get,
                GetProductPath(slug));

        conditionalRequest.Headers.IfNoneMatch.Add(
            entityTag);

        using var conditionalResponse =
            await client.SendAsync(
                conditionalRequest,
                TestContext.Current
                    .CancellationToken);

        Assert.Equal(
            HttpStatusCode.NotModified,
            conditionalResponse.StatusCode);

        var responseBody =
            await conditionalResponse.Content
                .ReadAsByteArrayAsync(
                    TestContext.Current
                        .CancellationToken);

        Assert.Empty(responseBody);
    }

    [Fact]
    public async Task OpenApiContainsPublishedProductRoute()
    {
        using var client =
            _fixture.CreateClient(
                authenticated: false,
                authorized: false);

        using var response = await client.GetAsync(
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

    private async Task PersistAsync(
        Product product)
    {
        await using var scope =
            _fixture.Services
                .CreateAsyncScope();

        var repository =
            scope.ServiceProvider
                .GetRequiredService<
                    IProductRepository>();

        var unitOfWork =
            scope.ServiceProvider
                .GetRequiredService<
                    ICatalogUnitOfWork>();

        repository.Add(product);

        await unitOfWork.SaveChangesAsync(
            TestContext.Current
                .CancellationToken);
    }

    private static Product CreateDraftProduct(
        string slug)
    {
        return Product.CreateDraft(
            ProductName.Create(
                "Storefront Product").Value,
            ProductSlug.Create(slug).Value,
            ProductDescription.Create(
                "Public storefront product.").Value,
            CreatedAtUtc);
    }

    private static Product CreatePublishedProduct(
        string slug)
    {
        var product = CreateDraftProduct(slug);

        var addVariantResult = product.AddVariant(
            Sku.Create(CreateSku("ACTIVE")).Value,
            VariantOptionCombination.Empty,
            CreatedAtUtc.AddMinutes(1));

        Assert.True(
            addVariantResult.IsSuccess,
            addVariantResult.Error?.Code);

        var publishResult = product.Publish(
            CreatedAtUtc.AddMinutes(2));

        Assert.True(
            publishResult.IsSuccess,
            publishResult.Error?.Code);

        return product;
    }

    private static (
        Product Product,
        string ActiveSku,
        string DiscontinuedSku)
        CreateProductWithDiscontinuedVariant(
            string slug)
    {
        var product = CreateDraftProduct(slug);

        var optionResult = product.DefineOption(
            OptionName.Create("Color").Value,
            displayOrder: 0);

        Assert.True(
            optionResult.IsSuccess,
            optionResult.Error?.Code);

        var activeSku = Sku.Create(
            CreateSku("ACTIVE")).Value;

        var discontinuedSku = Sku.Create(
            CreateSku("DISCONTINUED")).Value;

        var activeVariantResult = product.AddVariant(
            activeSku,
            CreateCombination(
                optionResult.Value,
                "Black"),
            CreatedAtUtc.AddMinutes(1));

        Assert.True(
            activeVariantResult.IsSuccess,
            activeVariantResult.Error?.Code);

        var discontinuedVariantResult =
            product.AddVariant(
                discontinuedSku,
                CreateCombination(
                    optionResult.Value,
                    "White"),
                CreatedAtUtc.AddMinutes(1));

        Assert.True(
            discontinuedVariantResult.IsSuccess,
            discontinuedVariantResult.Error?.Code);

        var publishResult = product.Publish(
            CreatedAtUtc.AddMinutes(2));

        Assert.True(
            publishResult.IsSuccess,
            publishResult.Error?.Code);

        var discontinueResult =
            product.DiscontinueVariant(
                discontinuedVariantResult.Value,
                CreatedAtUtc.AddMinutes(3));

        Assert.True(
            discontinueResult.IsSuccess,
            discontinueResult.Error?.Code);

        return (
            product,
            activeSku.Value,
            discontinuedSku.Value);
    }

    private static VariantOptionCombination
        CreateCombination(
            ProductOptionId optionId,
            string value)
    {
        var selection = OptionSelection.Create(
            optionId,
            OptionValue.Create(value).Value);

        return VariantOptionCombination.Create(
            [selection]).Value;
    }

    private static string CreateSlug()
    {
        return string.Concat(
            "storefront-http-product-",
            Guid.CreateVersion7()
                .ToString("N"));
    }

    private static string CreateSku(
        string prefix)
    {
        return string.Concat(
            prefix,
            "-",
            Guid.CreateVersion7()
                .ToString("N"));
    }

    private static string GetProductPath(
        string slug)
    {
        return string.Concat(
            StorefrontProductsPath,
            "/",
            slug);
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

    private sealed record PublishedProductResponse(
        Guid ProductId,
        string Name,
        string Slug,
        PublishedProductVariantResponse[] Variants);

    private sealed record PublishedProductVariantResponse(
        Guid VariantId,
        string Sku);
}
