
namespace EventStoreCore;

public sealed class DbSubscription
{
    public string SubscriptionAssemblyQualifiedName { get; set; } = null!;
    public long Sequence { get; set; } = 0;
}
