namespace EventStoreCore.Scheduling;

internal static class ScheduledPayloadTypeIdentity
{
    public static string GetId(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return BuildId(type);
    }

    private static string BuildId(Type type)
    {
        var assemblyName = type.Assembly.GetName().Name
            ?? throw new InvalidOperationException($"Unable to resolve assembly name for scheduled payload type '{type}'.");

        if (!type.IsGenericType)
        {
            return $"{type.FullName}|{assemblyName}";
        }

        var genericDefinition = type.GetGenericTypeDefinition();
        var genericArguments = string.Join(",", type.GetGenericArguments().Select(BuildId));
        return $"{genericDefinition.FullName}[{genericArguments}]|{assemblyName}";
    }
}
