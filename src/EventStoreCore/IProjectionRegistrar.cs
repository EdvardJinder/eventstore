using EventStoreCore.Abstractions;

namespace EventStoreCore;


internal interface IProjectionRegistrar
{
    void AddProjection<TProjection, TSnapshot>(ProjectionMode mode, Action<IProjectionOptions>? configure)
        where TProjection : IProjection<TSnapshot>, new()
        where TSnapshot : class, new();
}
