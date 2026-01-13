using Microsoft.Extensions.DependencyInjection;
using Refit;

namespace EventStoreCore.SDK;


/// <summary>
/// Factory for creating projection admin clients.
/// </summary>
public static class EndpointClientExtensions
{
    
    /// <summary>
    /// Adds the projection admin client to the service collection for dependency injection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configuration action for client options.</param>
    /// <returns>The IHttpClientBuilder for further configuration.</returns>
    public static IHttpClientBuilder AddEventStoreEndpointsClient(
        this IServiceCollection services,
        Action<HttpClient> configureClient)
    {
        return services
            .AddRefitClient<IEventStoreEndpointsClient>()
            .ConfigureHttpClient(client =>
            {
                configureClient(client);
            });
    }
}
