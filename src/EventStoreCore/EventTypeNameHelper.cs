using System.Text;

namespace EventStoreCore;

internal static class EventTypeNameHelper
{
    internal static string GetDefaultName(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        return ToSnakeCase(eventType.Name);
    }

    internal static string GetDefaultNameFromAqn(string? assemblyQualifiedName)
    {
        var simpleName = GetSimpleNameFromAqn(assemblyQualifiedName);
        return ToSnakeCase(simpleName);
    }

    internal static string? GetFullNameFromAqn(string? assemblyQualifiedName)
    {
        if (string.IsNullOrWhiteSpace(assemblyQualifiedName))
        {
            return null;
        }

        assemblyQualifiedName = assemblyQualifiedName.Trim();

        var bracketDepth = 0;
        var commaIndex = -1;

        for (var i = 0; i < assemblyQualifiedName.Length; i++)
        {
            var current = assemblyQualifiedName[i];
            if (current == '[')
            {
                bracketDepth++;
                continue;
            }

            if (current == ']')
            {
                bracketDepth = Math.Max(0, bracketDepth - 1);
                continue;
            }

            if (current == ',' && bracketDepth == 0)
            {
                commaIndex = i;
                break;
            }
        }

        var typePart = commaIndex >= 0
            ? assemblyQualifiedName[..commaIndex]
            : assemblyQualifiedName;

        return string.IsNullOrWhiteSpace(typePart) ? null : typePart.Trim();
    }

    internal static string? GetSimpleNameFromAqn(string? assemblyQualifiedName)
    {
        var fullName = GetFullNameFromAqn(assemblyQualifiedName);
        return GetSimpleName(fullName);
    }

    internal static string? GetSimpleName(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return null;
        }

        var name = fullName.Trim();
        var separatorIndex = Math.Max(name.LastIndexOf('.'), name.LastIndexOf('+'));
        if (separatorIndex >= 0 && separatorIndex < name.Length - 1)
        {
            name = name[(separatorIndex + 1)..];
        }

        var genericIndex = name.IndexOf('`');
        if (genericIndex >= 0)
        {
            name = name[..genericIndex];
        }

        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    internal static string ToSnakeCase(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        return ToSnakeCase(eventType.Name);
    }

    internal static string ToSnakeCase(string? name)
    {
        var simpleName = GetSimpleName(name);
        if (string.IsNullOrWhiteSpace(simpleName))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(simpleName.Length + 8);
        for (var i = 0; i < simpleName.Length; i++)
        {
            var current = simpleName[i];
            if (char.IsUpper(current))
            {
                var hasPrevious = i > 0;
                var hasNext = i + 1 < simpleName.Length;
                var previous = hasPrevious ? simpleName[i - 1] : '\0';
                var next = hasNext ? simpleName[i + 1] : '\0';

                if (hasPrevious && (char.IsLower(previous) || (char.IsUpper(previous) && hasNext && char.IsLower(next))))
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(current));
                continue;
            }

            builder.Append(char.ToLowerInvariant(current));
        }

        return builder.ToString();
    }
}
