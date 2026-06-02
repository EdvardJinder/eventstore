namespace EventStoreCore.Scheduling;

internal static class SchedulerRegistrationName
{
    public static string CreateDefault(string providerName, Type eventType)
    {
        var eventName = eventType.FullName ?? eventType.Name;
        return $"{providerName}:{eventName}";
    }

    public static string Validate(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Scheduler registration names must not be null, empty, or whitespace.", nameof(name));
        }

        return name;
    }
}
