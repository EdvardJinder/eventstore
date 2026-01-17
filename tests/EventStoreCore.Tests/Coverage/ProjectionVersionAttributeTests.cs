using EventStoreCore.Abstractions;

namespace EventStoreCore.Tests;

public class ProjectionVersionAttributeTests
{
    [Fact]
    public void Constructor_Throws_ForNonPositiveVersion()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProjectionVersionAttribute(0));
    }

    [Fact]
    public void Constructor_SetsVersion()
    {
        var attribute = new ProjectionVersionAttribute(3);

        Assert.Equal(3, attribute.Version);
    }
}
