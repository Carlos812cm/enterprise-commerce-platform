using Commerce.Domain;
using Xunit;

namespace Commerce.Domain.UnitTests;

public sealed class DomainErrorTests
{
    [Fact]
    public void ValidationFactoryCreatesValidationError()
    {
        var error = DomainError.Validation(
            " Catalog.Product.NameRequired ",
            " Product name is required. ");

        Assert.Equal(
            "Catalog.Product.NameRequired",
            error.Code);

        Assert.Equal(
            "Product name is required.",
            error.Description);

        Assert.Equal(
            ErrorType.Validation,
            error.Type);
    }

    [Fact]
    public void EmptyCodeIsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            DomainError.Validation(
                " ",
                "Product name is required."));
    }

    [Fact]
    public void EmptyDescriptionIsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            DomainError.Validation(
                "Catalog.Product.NameRequired",
                " "));
    }
}