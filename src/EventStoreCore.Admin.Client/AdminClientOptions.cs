namespace EventStoreCore.Admin.Client;

/// <summary>
/// Configuration options for the projection admin client.
/// </summary>
public sealed class AdminClientOptions
{
    /// <summary>
    /// The base URL of the EventStore admin API.
    /// </summary>
    public string BaseUrl { get; set; } = null!;

    /// <summary>
    /// Optional callback to configure HTTP requests (e.g., add authentication headers).
    /// </summary>
    public Func<HttpRequestMessage, Task>? ConfigureRequest { get; set; }

    /// <summary>
    /// Optional timeout for HTTP requests.
    /// Default is 30 seconds.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}
