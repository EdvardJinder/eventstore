namespace EventStoreCore.Scheduling;

internal sealed class SchedulerOptions
{
    public List<IEventScheduleRegistration> Registrations { get; } = [];

    public Dictionary<string, Type> PayloadTypes { get; } = new(StringComparer.Ordinal);

    public void RegisterPayloadType(Type type)
    {
        PayloadTypes[ScheduledPayloadTypeIdentity.GetId(type)] = type;
    }
}
