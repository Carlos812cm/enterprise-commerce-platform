using System.Text.Json;
using Catalog.Application.Abstractions.Persistence;
using Catalog.Domain.Products;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using Catalog.Domain.Products.Events;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Infrastructure.IntegrationTests;

public sealed class TransactionalOutboxIntegrationTests :
    IClassFixture<CatalogPostgreSqlFixture>
{

    private const string CacheInvalidationMessageType =
        "catalog.storefront-product-cache-invalidate.v1";

    private const string ProductPublishedMessageType =
        "catalog.product-published.v1";

    private static readonly DateTimeOffset CreatedAtUtc =
        new(
            2026,
            8,
            12,
            12,
            0,
            0,
            TimeSpan.Zero);

    private static readonly DateTimeOffset PublishedAtUtc =
        CreatedAtUtc.AddMinutes(2);

    private readonly CatalogPostgreSqlFixture _fixture;

    public TransactionalOutboxIntegrationTests(
        CatalogPostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task OutboxFailureRollsBackPublicationAndRetryDoesNotDuplicateMessages()
    {
        await using var serviceProvider =
            _fixture.CreateServiceProvider(
                TimeProvider.System);

        var slug =
            $"outbox-rollback-{Guid.CreateVersion7():N}";

        var product = Product.CreateDraft(
            ProductName.Create(
                "Transactional Outbox Rollback Product").Value,
            ProductSlug.Create(slug).Value,
            ProductDescription.Empty,
            CreatedAtUtc);

        var addVariantResult =
            product.AddVariant(
                Sku.Create(
                    $"ROLLBACK-{Guid.CreateVersion7():N}").Value,
                VariantOptionCombination.Empty,
                CreatedAtUtc.AddMinutes(1));

        Assert.True(
            addVariantResult.IsSuccess,
            addVariantResult.Error?.Code);

        await using (var seedScope =
            serviceProvider.CreateAsyncScope())
        {
            var repository =
                seedScope.ServiceProvider
                    .GetRequiredService<
                        IProductRepository>();

            var unitOfWork =
                seedScope.ServiceProvider
                    .GetRequiredService<
                        ICatalogUnitOfWork>();

            repository.Add(product);

            await unitOfWork.SaveChangesAsync(
                TestContext.Current
                    .CancellationToken);
        }

        await using var publicationScope =
            serviceProvider.CreateAsyncScope();

        var publicationRepository =
            publicationScope.ServiceProvider
                .GetRequiredService<
                    IProductRepository>();

        var publicationUnitOfWork =
            publicationScope.ServiceProvider
                .GetRequiredService<
                    ICatalogUnitOfWork>();

        var loaded =
            await publicationRepository.GetByIdAsync(
                product.Id,
                TestContext.Current
                    .CancellationToken);

        Assert.NotNull(loaded);

        var publishResult =
            loaded.Publish(
                PublishedAtUtc);

        Assert.True(
            publishResult.IsSuccess,
            publishResult.Error?.Code);

        publicationRepository.Update(
            loaded);

        Assert.Collection(
            loaded.DomainEvents,
            domainEvent =>
                Assert.IsType<
                    ProductVariantActivatedDomainEvent>(
                    domainEvent),
            domainEvent =>
                Assert.IsType<
                    ProductPublishedDomainEvent>(
                    domainEvent));

        try
        {
            await InstallOutboxFailureTriggerAsync(
                serviceProvider);

            await AssertOutboxFailureTriggerIsActiveAsync(
                serviceProvider);

            var exception =
                await Assert.ThrowsAsync<
                    DbUpdateException>(
                    () => publicationUnitOfWork
                        .SaveChangesAsync(
                            TestContext.Current
                                .CancellationToken));

            var postgresException =
                Assert.IsType<PostgresException>(
                    exception.InnerException);

            Assert.Equal(
                "P0001",
                postgresException.SqlState);

            Assert.Collection(
                loaded.DomainEvents,
                domainEvent =>
                    Assert.IsType<
                        ProductVariantActivatedDomainEvent>(
                        domainEvent),
                domainEvent =>
                    Assert.IsType<
                        ProductPublishedDomainEvent>(
                        domainEvent));

            var rolledBackState =
                await ReadProductPersistenceStateAsync(
                    serviceProvider,
                    product.Id.Value);

            Assert.Equal(
                "Draft",
                rolledBackState.Status);

            Assert.Null(
                rolledBackState.PublishedAtUtc);

            Assert.Equal(
                0,
                rolledBackState.ActiveVariantCount);

            var failedMessages =
                await ReadOutboxMessagesAsync(
                    serviceProvider,
                    product.Id.Value);

            Assert.Empty(
                failedMessages);
        }
        finally
        {
            await RemoveOutboxFailureTriggerAsync(
                serviceProvider);
        }

        await publicationUnitOfWork.SaveChangesAsync(
            TestContext.Current
                .CancellationToken);

        Assert.Empty(
            loaded.DomainEvents);

        var committedState =
            await ReadProductPersistenceStateAsync(
                serviceProvider,
                product.Id.Value);

        Assert.Equal(
            "Published",
            committedState.Status);

        Assert.Equal(
            PublishedAtUtc,
            committedState.PublishedAtUtc);

        Assert.Equal(
            1,
            committedState.ActiveVariantCount);

        var committedMessages =
            await ReadOutboxMessagesAsync(
                serviceProvider,
                product.Id.Value);

        Assert.Equal(
            2,
            committedMessages.Count);

        Assert.Equal(
            2,
            committedMessages
                .Select(message => message.Id)
                .Distinct()
                .Count());

        Assert.Collection(
            committedMessages,
            message =>
                AssertMessage(
                    message,
                    ProductPublishedMessageType,
                    product.Id.Value,
                    slug),
            message =>
                AssertMessage(
                    message,
                    CacheInvalidationMessageType,
                    product.Id.Value,
                    slug));
    }

    private static async Task InstallOutboxFailureTriggerAsync(
        IServiceProvider serviceProvider)
    {
        var dataSource =
            serviceProvider.GetRequiredService<
                NpgsqlDataSource>();

        await using var connection =
            await dataSource.OpenConnectionAsync(
                TestContext.Current
                    .CancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            CREATE OR REPLACE FUNCTION
                catalog.reject_test_product_published_outbox()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $function$
            BEGIN
                IF NEW.type =
                    'catalog.product-published.v1'
                THEN
                    RAISE EXCEPTION
                        'Forced transactional outbox failure.'
                        USING ERRCODE = 'P0001';
                END IF;

                RETURN NEW;
            END;
            $function$;

            CREATE OR REPLACE TRIGGER
                trg_test_reject_product_published_outbox
            BEFORE INSERT
            ON catalog.outbox_messages
            FOR EACH ROW
            EXECUTE FUNCTION
                catalog.reject_test_product_published_outbox();
            """;

        _ = await command.ExecuteNonQueryAsync(
            TestContext.Current
                .CancellationToken);
    }

    private static async Task AssertOutboxFailureTriggerIsActiveAsync(
        IServiceProvider serviceProvider)
    {
        var dataSource =
            serviceProvider.GetRequiredService<
                NpgsqlDataSource>();

        await using var connection =
            await dataSource.OpenConnectionAsync(
                TestContext.Current
                    .CancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            INSERT INTO catalog.outbox_messages (
                id,
                type,
                payload,
                occurred_at_utc
            )
            VALUES (
                @id,
                'catalog.product-published.v1',
                '{}'::jsonb,
                @occurred_at_utc
            );
            """;

        command.Parameters.AddWithValue(
            "id",
            Guid.CreateVersion7());

        command.Parameters.AddWithValue(
            "occurred_at_utc",
            PublishedAtUtc);

        var exception =
            await Assert.ThrowsAsync<
                PostgresException>(
                () => command.ExecuteNonQueryAsync(
                    TestContext.Current
                        .CancellationToken));

        Assert.Equal(
            "P0001",
            exception.SqlState);
    }

    private static async Task RemoveOutboxFailureTriggerAsync(
        IServiceProvider serviceProvider)
    {
        var dataSource =
            serviceProvider.GetRequiredService<
                NpgsqlDataSource>();

        await using var connection =
            await dataSource.OpenConnectionAsync(
                TestContext.Current
                    .CancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            DROP TRIGGER IF EXISTS
                trg_test_reject_product_published_outbox
            ON catalog.outbox_messages;

            DROP FUNCTION IF EXISTS
                catalog.reject_test_product_published_outbox();
            """;

        _ = await command.ExecuteNonQueryAsync(
            TestContext.Current
                .CancellationToken);
    }

    private static async Task<ProductPersistenceSnapshot>
        ReadProductPersistenceStateAsync(
            IServiceProvider serviceProvider,
            Guid productId)
    {
        var dataSource =
            serviceProvider.GetRequiredService<
                NpgsqlDataSource>();

        await using var connection =
            await dataSource.OpenConnectionAsync(
                TestContext.Current
                    .CancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            SELECT
                product.status,
                product.published_at_utc,
                (
                    COUNT(*) FILTER (
                        WHERE variant.status = 'Active'
                    )
                )::integer
            FROM catalog.products AS product
            INNER JOIN catalog.product_variants AS variant
                ON variant.product_id = product.id
            WHERE product.id = @product_id
            GROUP BY
                product.status,
                product.published_at_utc;
            """;

        command.Parameters.AddWithValue(
            "product_id",
            productId);

        await using var reader =
            await command.ExecuteReaderAsync(
                TestContext.Current
                    .CancellationToken);

        Assert.True(
            await reader.ReadAsync(
                TestContext.Current
                    .CancellationToken));

        var publishedAtUtc =
            reader.IsDBNull(1)
                ? (DateTimeOffset?)null
                : reader.GetFieldValue<
                    DateTimeOffset>(1);

        var snapshot =
            new ProductPersistenceSnapshot(
                reader.GetString(0),
                publishedAtUtc,
                reader.GetInt32(2));

        Assert.False(
            await reader.ReadAsync(
                TestContext.Current
                    .CancellationToken));

        return snapshot;
    }

    private sealed record ProductPersistenceSnapshot(
        string Status,
        DateTimeOffset? PublishedAtUtc,
        int ActiveVariantCount);

    [Fact]
    public async Task PublicationPersistsProductAndTwoOutboxMessages()
    {
        await using var serviceProvider =
            _fixture.CreateServiceProvider(
                TimeProvider.System);

        var slug =
            $"outbox-{Guid.CreateVersion7():N}";

        var product = Product.CreateDraft(
            ProductName.Create(
                "Transactional Outbox Product").Value,
            ProductSlug.Create(slug).Value,
            ProductDescription.Empty,
            CreatedAtUtc);

        var addVariantResult =
            product.AddVariant(
                Sku.Create(
                    $"OUTBOX-{Guid.CreateVersion7():N}").Value,
                VariantOptionCombination.Empty,
                CreatedAtUtc.AddMinutes(1));

        Assert.True(
            addVariantResult.IsSuccess,
            addVariantResult.Error?.Code);

        await using (var seedScope =
            serviceProvider.CreateAsyncScope())
        {
            var repository =
                seedScope.ServiceProvider
                    .GetRequiredService<
                        IProductRepository>();

            var unitOfWork =
                seedScope.ServiceProvider
                    .GetRequiredService<
                        ICatalogUnitOfWork>();

            repository.Add(product);

            await unitOfWork.SaveChangesAsync(
                TestContext.Current
                    .CancellationToken);

            Assert.Empty(
                product.DomainEvents);
        }

        await using (var publicationScope =
            serviceProvider.CreateAsyncScope())
        {
            var repository =
                publicationScope.ServiceProvider
                    .GetRequiredService<
                        IProductRepository>();

            var unitOfWork =
                publicationScope.ServiceProvider
                    .GetRequiredService<
                        ICatalogUnitOfWork>();

            var loaded =
                await repository.GetByIdAsync(
                    product.Id,
                    TestContext.Current
                        .CancellationToken);

            Assert.NotNull(loaded);

            var publishResult =
                loaded.Publish(
                    PublishedAtUtc);

            Assert.True(
                publishResult.IsSuccess,
                publishResult.Error?.Code);

            repository.Update(loaded);

            await unitOfWork.SaveChangesAsync(
                TestContext.Current
                    .CancellationToken);

            Assert.Empty(
                loaded.DomainEvents);
        }

        await using (var verificationScope =
            serviceProvider.CreateAsyncScope())
        {
            var repository =
                verificationScope.ServiceProvider
                    .GetRequiredService<
                        IProductRepository>();

            var persisted =
                await repository.GetByIdAsync(
                    product.Id,
                    TestContext.Current
                        .CancellationToken);

            Assert.NotNull(persisted);

            Assert.Equal(
                ProductStatus.Published,
                persisted.Status);

            Assert.Equal(
                PublishedAtUtc,
                persisted.PublishedAtUtc);

            var variant =
                Assert.Single(
                    persisted.Variants);

            Assert.Equal(
                ProductVariantStatus.Active,
                variant.Status);

            Assert.Equal(
                PublishedAtUtc,
                variant.ActivatedAtUtc);

            Assert.Empty(
                persisted.DomainEvents);
        }

        var messages =
            await ReadOutboxMessagesAsync(
                serviceProvider,
                product.Id.Value);

        Assert.Equal(
            2,
            messages.Count);

        Assert.NotEqual(
            messages[0].Id,
            messages[1].Id);

        Assert.Collection(
            messages,
            message =>
                AssertMessage(
                    message,
                    ProductPublishedMessageType,
                    product.Id.Value,
                    slug),
            message =>
                AssertMessage(
                    message,
                    CacheInvalidationMessageType,
                    product.Id.Value,
                    slug));
    }

    private static async Task<List<OutboxMessageSnapshot>>
        ReadOutboxMessagesAsync(
            IServiceProvider serviceProvider,
            Guid productId)
    {
        var dataSource =
            serviceProvider.GetRequiredService<
                NpgsqlDataSource>();

        await using var connection =
            await dataSource.OpenConnectionAsync(
                TestContext.Current
                    .CancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            SELECT
                id,
                type,
                payload::text,
                occurred_at_utc,
                attempt_count,
                processed_at_utc IS NULL
                    AND dead_lettered_at_utc IS NULL,
                lock_owner IS NULL
                    AND locked_until_utc IS NULL
            FROM catalog.outbox_messages
            WHERE (payload ->> 'productId')::uuid =
                @product_id
            ORDER BY type;
            """;

        command.Parameters.AddWithValue(
            "product_id",
            productId);

        await using var reader =
            await command.ExecuteReaderAsync(
                TestContext.Current
                    .CancellationToken);

        var messages =
            new List<OutboxMessageSnapshot>();

        while (await reader.ReadAsync(
            TestContext.Current
                .CancellationToken))
        {
            messages.Add(
                new OutboxMessageSnapshot(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetFieldValue<DateTimeOffset>(3),
                    reader.GetInt32(4),
                    reader.GetBoolean(5),
                    reader.GetBoolean(6)));
        }

        return messages;
    }

    [Fact]
    public async Task RehydrationAloneDoesNotEnrollProductForOutboxPersistence()
    {
        await using var serviceProvider =
            _fixture.CreateServiceProvider(
                TimeProvider.System);

        var slug =
            $"outbox-enrollment-{Guid.CreateVersion7():N}";

        var product = Product.CreateDraft(
            ProductName.Create(
                "Explicit Outbox Enrollment Product").Value,
            ProductSlug.Create(slug).Value,
            ProductDescription.Empty,
            CreatedAtUtc);

        var addVariantResult =
            product.AddVariant(
                Sku.Create(
                    $"ENROLL-{Guid.CreateVersion7():N}").Value,
                VariantOptionCombination.Empty,
                CreatedAtUtc.AddMinutes(1));

        Assert.True(
            addVariantResult.IsSuccess,
            addVariantResult.Error?.Code);

        await using (var seedScope =
            serviceProvider.CreateAsyncScope())
        {
            var seedRepository =
                seedScope.ServiceProvider
                    .GetRequiredService<
                        IProductRepository>();

            var seedUnitOfWork =
                seedScope.ServiceProvider
                    .GetRequiredService<
                        ICatalogUnitOfWork>();

            seedRepository.Add(product);

            await seedUnitOfWork.SaveChangesAsync(
                TestContext.Current
                    .CancellationToken);
        }

        await using var publicationScope =
            serviceProvider.CreateAsyncScope();

        var repository =
            publicationScope.ServiceProvider
                .GetRequiredService<
                    IProductRepository>();

        var unitOfWork =
            publicationScope.ServiceProvider
                .GetRequiredService<
                    ICatalogUnitOfWork>();

        var loaded =
            await repository.GetByIdAsync(
                product.Id,
                TestContext.Current
                    .CancellationToken);

        Assert.NotNull(loaded);

        var publishResult =
            loaded.Publish(
                PublishedAtUtc);

        Assert.True(
            publishResult.IsSuccess,
            publishResult.Error?.Code);

        Assert.Collection(
            loaded.DomainEvents,
            domainEvent =>
                Assert.IsType<
                    ProductVariantActivatedDomainEvent>(
                    domainEvent),
            domainEvent =>
                Assert.IsType<
                    ProductPublishedDomainEvent>(
                    domainEvent));

        await unitOfWork.SaveChangesAsync(
            TestContext.Current
                .CancellationToken);

        Assert.Collection(
            loaded.DomainEvents,
            domainEvent =>
                Assert.IsType<
                    ProductVariantActivatedDomainEvent>(
                    domainEvent),
            domainEvent =>
                Assert.IsType<
                    ProductPublishedDomainEvent>(
                    domainEvent));

        var stateBeforeEnrollment =
            await ReadProductPersistenceStateAsync(
                serviceProvider,
                product.Id.Value);

        Assert.Equal(
            "Draft",
            stateBeforeEnrollment.Status);

        Assert.Null(
            stateBeforeEnrollment.PublishedAtUtc);

        Assert.Equal(
            0,
            stateBeforeEnrollment.ActiveVariantCount);

        var messagesBeforeEnrollment =
            await ReadOutboxMessagesAsync(
                serviceProvider,
                product.Id.Value);

        Assert.Empty(
            messagesBeforeEnrollment);

        repository.Update(
            loaded);

        await unitOfWork.SaveChangesAsync(
            TestContext.Current
                .CancellationToken);

        Assert.Empty(
            loaded.DomainEvents);

        var committedState =
            await ReadProductPersistenceStateAsync(
                serviceProvider,
                product.Id.Value);

        Assert.Equal(
            "Published",
            committedState.Status);

        Assert.Equal(
            PublishedAtUtc,
            committedState.PublishedAtUtc);

        Assert.Equal(
            1,
            committedState.ActiveVariantCount);

        var committedMessages =
            await ReadOutboxMessagesAsync(
                serviceProvider,
                product.Id.Value);

        Assert.Equal(
            2,
            committedMessages.Count);

        Assert.Collection(
            committedMessages,
            message =>
                AssertMessage(
                    message,
                    ProductPublishedMessageType,
                    product.Id.Value,
                    slug),
            message =>
                AssertMessage(
                    message,
                    CacheInvalidationMessageType,
                    product.Id.Value,
                    slug));
    }

    private static void AssertMessage(
        OutboxMessageSnapshot message,
        string expectedMessageType,
        Guid expectedProductId,
        string expectedSlug)
    {
        Assert.Equal(
            expectedMessageType,
            message.MessageType);

        Assert.Equal(
            PublishedAtUtc,
            message.OccurredAtUtc);

        Assert.Equal(
            0,
            message.AttemptCount);

        Assert.True(
            message.IsPending);

        Assert.True(
            message.HasNoLease);

        using var payload =
            JsonDocument.Parse(
                message.Payload);

        var root =
            payload.RootElement;

        Assert.Equal(
            expectedProductId,
            root.GetProperty(
                "productId").GetGuid());

        Assert.Equal(
            expectedSlug,
            root.GetProperty(
                "slug").GetString());

        Assert.Equal(
            PublishedAtUtc,
            root.GetProperty(
                "publishedAtUtc")
                .GetDateTimeOffset());
    }

    private sealed record OutboxMessageSnapshot(
        Guid Id,
        string MessageType,
        string Payload,
        DateTimeOffset OccurredAtUtc,
        int AttemptCount,
        bool IsPending,
        bool HasNoLease);
}
