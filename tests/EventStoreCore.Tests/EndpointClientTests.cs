using System.Net;
using EventStoreCore.SDK;
using Refit;

namespace EventStoreCore.Tests;

public sealed class EndpointClientTests
{
    [Fact]
    public async Task LookupMethodsExposeNotFoundResponsesWithoutThrowing()
    {
        using var httpClient = new HttpClient(new StubHandler(HttpStatusCode.NotFound))
        {
            BaseAddress = new Uri("https://event-store.test")
        };
        var client = RestService.For<IEventStoreEndpointsClient>(httpClient);

        using var projection = await client.GetProjectionAsync(
            "missing",
            TestContext.Current.CancellationToken);
        using var subscription = await client.GetSubscriptionAsync(
            "missing",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, projection.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, subscription.StatusCode);
        Assert.False(projection.IsSuccessful);
        Assert.False(subscription.IsSuccessful);
    }

    private sealed class StubHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                RequestMessage = request
            });
        }
    }
}
