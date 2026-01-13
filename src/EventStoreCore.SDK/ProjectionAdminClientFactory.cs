using Microsoft.Extensions.DependencyInjection;
using Refit;

namespace EventStoreCore.Admin.Client;

/// <summary>
/// Factory for creating projection admin clients.
/// </summary>
public static class ProjectionAdminClientFactory
{
    /// <summary>
    /// Creates a new projection admin client with the specified options.
    /// </summary>
    /// <param name="options">The client configuration options.</param>
    /// <returns>A configured IProjectionAdminClient instance.</returns>
    public static IProjectionAdminClient Create(AdminClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options.BaseUrl);

        var httpClient = new HttpClient(new AuthDelegatingHandler(options))
        {
            BaseAddress = new Uri(options.BaseUrl),
            Timeout = options.Timeout
        };

        return RestService.For<IProjectionAdminClient>(httpClient);
    }

    /// <summary>
    /// Creates a new projection admin client with a simple base URL.
    /// </summary>
    /// <param name="baseUrl">The base URL of the admin API.</param>
    /// <returns>A configured IProjectionAdminClient instance.</returns>
    public static IProjectionAdminClient Create(string baseUrl)
    {
        return Create(new AdminClientOptions { BaseUrl = baseUrl });
    }

    /// <summary>
    /// Adds the projection admin client to the service collection for dependency injection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configuration action for client options.</param>
    /// <returns>The IHttpClientBuilder for further configuration.</returns>
    public static IHttpClientBuilder AddProjectionAdminClient(
        this IServiceCollection services,
        Action<AdminClientOptions> configure)
    {
        var options = new AdminClientOptions();
        configure(options);

        return services
            .AddRefitClient<IProjectionAdminClient>()
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri(options.BaseUrl);
                client.Timeout = options.Timeout;
            })
            .AddHttpMessageHandler(() => new AuthDelegatingHandler(options));
    }
}

/// <summary>
/// Delegating handler that applies authentication configuration to requests.
/// </summary>
internal sealed class AuthDelegatingHandler : DelegatingHandler
{
    private readonly AdminClientOptions _options;

    public AuthDelegatingHandler(AdminClientOptions options)
        : base(new HttpClientHandler())
    {
        _options = options;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (_options.ConfigureRequest != null)
        {
            await _options.ConfigureRequest(request);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
