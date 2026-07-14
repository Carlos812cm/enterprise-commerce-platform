using Commerce.Domain;
using Xunit;

namespace Commerce.Domain.UnitTests;

public sealed class ResultTests
{
    [Fact]
    public void SuccessContainsNoError()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Null(result.Error);
    }

    [Fact]
    public void FailureContainsProvidedError()
    {
        var error = DomainError.Validation(
            "Catalog.Product.NameRequired",
            "Product name is required.");

        var result = Result.Failure(error);

        Assert.True(result.IsFailure);
        Assert.False(result.IsSuccess);
        Assert.Same(error, result.Error);
    }

    [Fact]
    public void GenericSuccessExposesValue()
    {
        const string expected = "catalog-value";

        var result = Result.Success(expected);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value);
    }

    [Fact]
    public void GenericFailureRejectsValueAccess()
    {
        var error = DomainError.Validation(
            "Catalog.Product.NameRequired",
            "Product name is required.");

        var result = Result.Failure<string>(error);

        Assert.Throws<InvalidOperationException>(() =>
        {
            _ = result.Value;
        });
    }
}
