using EventStoreCore.Abstractions;
using System.Runtime.CompilerServices;

namespace EventStoreCore.Tests;

public sealed class PublicApiVisibilityContractTests
{
    [Fact]
    public void Projection_version_is_configured_exclusively_by_attribute()
    {
        Assert.Null(typeof(IProjectionOptions).GetMethod("Version"));
        Assert.Equal(3, new ProjectionVersionAttribute(3).Version);
    }

    [Fact]
    public void Projection_matching_helpers_are_not_public_api()
    {
        Assert.Null(typeof(IProjectionOptions).GetMethod("IsHandeled"));
        Assert.Null(typeof(IProjectionOptions).GetMethod("IsHandled"));
        Assert.False(typeof(ProjectionOptions).IsPublic);
        Assert.Null(typeof(ProjectionOptions).GetMethod("IsHandeled"));
    }

    [Theory]
    [InlineData(typeof(DbEvent))]
    [InlineData(typeof(DbStream))]
    [InlineData(typeof(DbSnapshot))]
    [InlineData(typeof(DbProjectionStatus))]
    [InlineData(typeof(DbSubscription))]
    [InlineData(typeof(DbSchedulerEventApplication))]
    [InlineData(typeof(DbContextEventStore))]
    [InlineData(typeof(DbContextEventLogReader))]
    [InlineData(typeof(DbContextStream))]
    public void Persistence_and_ef_implementation_types_are_not_public(Type type)
    {
        Assert.False(type.IsPublic);
    }

    [Fact]
    public void Public_stream_contract_does_not_expose_concrete_ef_types()
    {
        var publicStreamTypes = typeof(IStream).Assembly
            .GetExportedTypes()
            .Where(type => type.Name.Contains("Stream", StringComparison.Ordinal))
            .ToArray();

        Assert.Contains(typeof(IReadOnlyStream), publicStreamTypes);
        Assert.Contains(typeof(IReadOnlyStream<>), publicStreamTypes);
        Assert.Contains(typeof(IStream), publicStreamTypes);
        Assert.Contains(typeof(IStream<>), publicStreamTypes);
    }

    [Fact]
    public void Existing_outbox_persistence_types_remain_public_for_compatibility()
    {
        Assert.True(typeof(DbOutboxMessage).IsPublic);
        Assert.True(typeof(DbOutboxSubscription).IsPublic);
    }

    [Fact]
    public void Official_relational_providers_receive_internal_persistence_access()
    {
        var friendAssemblies = typeof(IEventStoreBuilder).Assembly
            .GetCustomAttributes(typeof(InternalsVisibleToAttribute), inherit: false)
            .Cast<InternalsVisibleToAttribute>()
            .Select(attribute => attribute.AssemblyName)
            .ToArray();

        Assert.Contains("EventStoreCore.Postgres", friendAssemblies);
        Assert.Contains("EventStoreCore.Sqlite", friendAssemblies);
        Assert.Contains("EventStoreCore.SqlServer", friendAssemblies);
    }
}
