using EventStoreCore;

namespace EventStoreCore.Tests;

public class ProjectionOptionsTests
{
    private sealed class EventA;
    private sealed class EventB;
    private sealed class EventC;

    [Fact]
    public void PersistedFiltersCombineCategoriesAndAllowMultipleValues()
    {
        var tenant = Guid.NewGuid();
        var stream = Guid.NewGuid();
        var options = new ProjectionOptions();
        options.IncludeLogicalEventType("order_created");
        options.IncludeLogicalEventType("order_updated");
        options.IncludeStreamType("orders");
        options.IncludeStream(stream);
        options.IncludeTenant(tenant);

        var matching = new DbEvent
        {
            Type = typeof(EventA).AssemblyQualifiedName!,
            TypeName = "order_updated",
            StreamType = "orders",
            StreamId = stream,
            TenantId = tenant
        };

        Assert.True(options.MatchesPersisted(matching));
        Assert.False(options.MatchesPersisted(new DbEvent
        {
            Type = typeof(EventA).AssemblyQualifiedName!,
            TypeName = "order_updated",
            StreamType = "audit",
            StreamId = stream,
            TenantId = tenant
        }));
    }

    [Fact]
    public void PersistedFilterArgumentsAreValidated()
    {
        var options = new ProjectionOptions();

        Assert.Throws<ArgumentException>(() => options.IncludeLogicalEventType(" "));
        Assert.Throws<ArgumentNullException>(() => options.IncludeStreamType(null!));
        options.IncludeStreamType(string.Empty);
    }

    [Fact]
    public void MaterializedFiltersUseSameCategorySemantics()
    {
        var tenant = Guid.NewGuid();
        var stream = Guid.NewGuid();
        var options = new ProjectionOptions();
        options.Handles<EventA>();
        options.Handles<EventB>();
        options.IncludeLogicalEventType("order_created");
        options.IncludeLogicalEventType("order_updated");
        options.IncludeStreamType("orders");
        options.IncludeStream(stream);
        options.IncludeTenant(tenant);

        var matching = new DbEvent
        {
            Type = typeof(EventB).AssemblyQualifiedName!,
            TypeName = "order_updated",
            StreamType = "orders",
            StreamId = stream,
            TenantId = tenant,
            Data = "{}"
        }.ToEvent();

        Assert.True(options.Matches(matching));
        Assert.False(options.Matches(new DbEvent
        {
            Type = typeof(EventC).AssemblyQualifiedName!,
            TypeName = "order_updated",
            StreamType = "orders",
            StreamId = stream,
            TenantId = tenant,
            Data = "{}"
        }.ToEvent()));
    }

    [Fact]
    public void HandlesAll_IsDefault()
    {
        var options = new ProjectionOptions();

        Assert.True(options.IsHandled(typeof(EventA)));
        Assert.True(options.IsHandled(typeof(EventB)));
    }

    [Fact]
    public void Handles_SwitchesToWhitelist()
    {
        var options = new ProjectionOptions();
        options.Handles<EventA>();

        Assert.True(options.IsHandled(typeof(EventA)));
        Assert.False(options.IsHandled(typeof(EventB)));
    }

    [Fact]
    public void Ignores_ExcludesTypeInHandlesAllMode()
    {
        var options = new ProjectionOptions();
        options.Ignores<EventA>();

        Assert.False(options.IsHandled(typeof(EventA)));
        Assert.True(options.IsHandled(typeof(EventB)));
    }

    [Fact]
    public void Ignores_ExcludesTypeInHandlesMode()
    {
        var options = new ProjectionOptions();
        options.Handles<EventA>();
        options.Handles<EventB>();
        options.Ignores<EventB>();

        Assert.True(options.IsHandled(typeof(EventA)));
        Assert.False(options.IsHandled(typeof(EventB)));
        Assert.False(options.IsHandled(typeof(EventC)));
    }

    [Fact]
    public void ShouldIgnoreUnknown_FalseByDefault()
    {
        var options = new ProjectionOptions();

        Assert.False(options.ShouldIgnoreUnknown);
    }

    [Fact]
    public void ShouldIgnoreUnknown_TrueWhenHandlesUsed()
    {
        var options = new ProjectionOptions();
        options.Handles<EventA>();

        Assert.True(options.ShouldIgnoreUnknown);
    }

    [Fact]
    public void ShouldIgnoreUnknown_TrueWhenIgnoreUnknownCalled()
    {
        var options = new ProjectionOptions();
        options.IgnoreUnknown();

        Assert.True(options.ShouldIgnoreUnknown);
    }

    [Fact]
    public void ShouldIgnoreUnknown_TrueWhenHandlesAllAndIgnoreUnknown()
    {
        var options = new ProjectionOptions();
        options.HandlesAll();
        options.IgnoreUnknown();

        Assert.True(options.ShouldIgnoreUnknown);
    }
}
