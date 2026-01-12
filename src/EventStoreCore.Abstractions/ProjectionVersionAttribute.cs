namespace EventStoreCore.Abstractions;

/// <summary>
/// Specifies the version of a projection. When the version changes, 
/// the projection daemon can automatically trigger a rebuild.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ProjectionVersionAttribute : Attribute
{
    /// <summary>
    /// Gets the version number of the projection.
    /// </summary>
    public int Version { get; }

    /// <summary>
    /// Creates a new instance of the <see cref="ProjectionVersionAttribute"/>.
    /// </summary>
    /// <param name="version">The version number. Increment this to trigger an automatic rebuild.</param>
    public ProjectionVersionAttribute(int version)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(version);
        Version = version;
    }
}
