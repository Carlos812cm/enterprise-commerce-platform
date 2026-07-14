using Commerce.Domain;
using Xunit;

namespace Commerce.Domain.UnitTests;

public sealed class EntityTests
{
    [Fact]
    public void EntitiesWithSameTypeAndIdentifierAreEqual()
    {
        var id = Guid.CreateVersion7();

        var first = new TestEntity(id);
        var second = new TestEntity(id);

        Assert.Equal(first, second);
        Assert.Equal(
            first.GetHashCode(),
            second.GetHashCode());
    }

    [Fact]
    public void EntitiesWithDifferentIdentifiersAreNotEqual()
    {
        var first = new TestEntity(Guid.CreateVersion7());
        var second = new TestEntity(Guid.CreateVersion7());

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void EntitiesWithDifferentRuntimeTypesAreNotEqual()
    {
        var id = Guid.CreateVersion7();

        var first = new TestEntity(id);
        var second = new OtherTestEntity(id);

        Assert.False(first.Equals(second));
    }

    private sealed class TestEntity(Guid id)
        : Entity<Guid>(id);

    private sealed class OtherTestEntity(Guid id)
        : Entity<Guid>(id);
}
