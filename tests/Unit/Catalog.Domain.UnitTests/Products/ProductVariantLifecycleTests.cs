using Catalog.Domain.Products;
using Xunit;

namespace Catalog.Domain.UnitTests.Products;

public sealed class ProductVariantLifecycleTests
{
    [Fact]
    public void ActivateTransitionsDraftToActive()
    {
        var variant = CreateDraftVariant();
        var activatedAtUtc = CreateUtcTimestamp(12);

        var result = variant.Activate(activatedAtUtc);

        Assert.True(result.IsSuccess);

        Assert.Equal(
            ProductVariantStatus.Active,
            variant.Status);

        Assert.Equal(
            activatedAtUtc,
            variant.ActivatedAtUtc);

        Assert.Null(variant.DiscontinuedAtUtc);
    }

    [Fact]
    public void ActivateIsIdempotent()
    {
        var variant = CreateDraftVariant();
        var firstTimestamp = CreateUtcTimestamp(12);
        var secondTimestamp = CreateUtcTimestamp(13);

        variant.Activate(firstTimestamp);

        var result = variant.Activate(secondTimestamp);

        Assert.True(result.IsSuccess);

        Assert.Equal(
            firstTimestamp,
            variant.ActivatedAtUtc);
    }

    [Fact]
    public void ActivateFailsAfterDiscontinuation()
    {
        var variant = CreateDraftVariant();

        variant.Discontinue(CreateUtcTimestamp(12));

        var result =
            variant.Activate(CreateUtcTimestamp(13));

        Assert.True(result.IsFailure);

        Assert.Equal(
            "Catalog.Variant.CannotActivateDiscontinued",
            result.Error?.Code);

        Assert.Equal(
            ProductVariantStatus.Discontinued,
            variant.Status);
    }

    [Fact]
    public void DiscontinueTransitionsDraftToDiscontinued()
    {
        var variant = CreateDraftVariant();
        var discontinuedAtUtc = CreateUtcTimestamp(12);

        var result =
            variant.Discontinue(discontinuedAtUtc);

        Assert.True(result.IsSuccess);

        Assert.Equal(
            ProductVariantStatus.Discontinued,
            variant.Status);

        Assert.Null(variant.ActivatedAtUtc);

        Assert.Equal(
            discontinuedAtUtc,
            variant.DiscontinuedAtUtc);
    }

    [Fact]
    public void DiscontinueTransitionsActiveToDiscontinued()
    {
        var variant = CreateDraftVariant();
        var activatedAtUtc = CreateUtcTimestamp(12);
        var discontinuedAtUtc = CreateUtcTimestamp(13);

        variant.Activate(activatedAtUtc);

        var result =
            variant.Discontinue(discontinuedAtUtc);

        Assert.True(result.IsSuccess);

        Assert.Equal(
            ProductVariantStatus.Discontinued,
            variant.Status);

        Assert.Equal(
            activatedAtUtc,
            variant.ActivatedAtUtc);

        Assert.Equal(
            discontinuedAtUtc,
            variant.DiscontinuedAtUtc);
    }

    [Fact]
    public void DiscontinueRejectsTimestampBeforeActivation()
    {
        var variant = CreateDraftVariant();
        var activatedAtUtc = CreateUtcTimestamp(13);

        variant.Activate(activatedAtUtc);

        var result =
            variant.Discontinue(
                CreateUtcTimestamp(12));

        Assert.True(result.IsFailure);

        Assert.Equal(
            "Catalog.Variant.DiscontinuedBeforeActivation",
            result.Error?.Code);

        Assert.Equal(
            ProductVariantStatus.Active,
            variant.Status);

        Assert.Null(variant.DiscontinuedAtUtc);
    }

    [Fact]
    public void DiscontinueIsIdempotent()
    {
        var variant = CreateDraftVariant();
        var firstTimestamp = CreateUtcTimestamp(12);
        var secondTimestamp = CreateUtcTimestamp(13);

        variant.Discontinue(firstTimestamp);

        var result =
            variant.Discontinue(secondTimestamp);

        Assert.True(result.IsSuccess);

        Assert.Equal(
            firstTimestamp,
            variant.DiscontinuedAtUtc);
    }

    [Fact]
    public void ActivateRejectsNonUtcTimestamp()
    {
        var variant = CreateDraftVariant();

        var timestamp = new DateTimeOffset(
            2026,
            7,
            17,
            12,
            0,
            0,
            TimeSpan.FromHours(-6));

        Assert.Throws<ArgumentException>(() =>
            variant.Activate(timestamp));
    }

    [Fact]
    public void DiscontinueRejectsDefaultTimestamp()
    {
        var variant = CreateDraftVariant();

        Assert.Throws<ArgumentException>(() =>
            variant.Discontinue(default));
    }

    private static ProductVariant CreateDraftVariant()
    {
        return ProductVariant.Create(
            Sku.Create("sku-001").Value,
            VariantOptionCombination.Empty);
    }

    private static DateTimeOffset CreateUtcTimestamp(
        int hour)
    {
        return new DateTimeOffset(
            2026,
            7,
            17,
            hour,
            0,
            0,
            TimeSpan.Zero);
    }
}
