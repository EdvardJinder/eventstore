

namespace IM.EventStore;

public interface IEventStoreBuilder
{
    IEventStoreBuilder AddProjection<TProjection, TSnapshot>(Action<IProjectionOptions>? configure = null)
           where TProjection : IProjection<TSnapshot>, new()
           where TSnapshot : class, new();
  
}
