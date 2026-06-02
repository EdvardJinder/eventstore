namespace EventStoreCore.Scheduling;

internal sealed class SchedulerOptions
{
    public TimeSpan ClaimTimeout { get; set; } = TimeSpan.FromMinutes(5);

    public List<IEventSchedulerRegistration> Registrations { get; } = [];

    public bool ContainsRegistration(string providerName, Type eventType, string registrationName)
    {
        return Registrations.Any(x => x.ProviderName == providerName &&
                                      x.EventType == eventType &&
                                      x.RegistrationName == registrationName);
    }
}
