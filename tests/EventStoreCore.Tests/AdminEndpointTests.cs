using System.Net;
using System.Net.Http.Json;
using EventStoreCore.Abstractions;
using EventStoreCore.Admin;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace EventStoreCore.Tests;

/// <summary>
/// Integration tests for the EventStore Admin API endpoints.
/// Uses TestServer to test the HTTP layer with a mocked IProjectionManager.
/// </summary>
public class AdminEndpointTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private IProjectionManager _mockManager = null!;

    public async ValueTask InitializeAsync()
    {
        _mockManager = Substitute.For<IProjectionManager>();

        _host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddSingleton(_mockManager);
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapGroup("/api/eventstore/admin")
                                .MapEventStoreAdmin();
                        });
                    });
            })
            .Build();

        await _host.StartAsync();
        _client = _host.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    #region GET /projections Tests

    [Fact]
    public async Task GetAllProjections_ReturnsEmptyList_WhenNoProjections()
    {
        // Arrange
        _mockManager.GetAllStatusesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ProjectionStatusDto>());

        // Act
        var response = await _client.GetAsync("/api/eventstore/admin/projections",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var projections = await response.Content.ReadFromJsonAsync<List<ProjectionStatusDto>>(
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(projections);
        Assert.Empty(projections);
    }

    [Fact]
    public async Task GetAllProjections_ReturnsProjections_WhenProjectionsExist()
    {
        // Arrange
        var expectedProjections = new List<ProjectionStatusDto>
        {
            new ProjectionStatusDto(
                ProjectionName: "TestProjection",
                Version: 1,
                State: ProjectionState.Active,
                Position: 100,
                TotalEvents: 100,
                ProgressPercentage: 100.0,
                LastProcessedAt: DateTimeOffset.UtcNow,
                LastError: null,
                FailedEventSequence: null,
                RebuildStartedAt: null,
                RebuildCompletedAt: null)
        };
        _mockManager.GetAllStatusesAsync(Arg.Any<CancellationToken>())
            .Returns(expectedProjections);

        // Act
        var response = await _client.GetAsync("/api/eventstore/admin/projections",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var projections = await response.Content.ReadFromJsonAsync<List<ProjectionStatusDto>>(
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(projections);
        Assert.Single(projections);
        Assert.Equal("TestProjection", projections[0].ProjectionName);
        Assert.Equal(ProjectionState.Active, projections[0].State);
        Assert.Equal(100, projections[0].Position);
    }

    #endregion

    #region GET /projections/{name} Tests

    [Fact]
    public async Task GetProjection_ReturnsNotFound_WhenProjectionDoesNotExist()
    {
        // Arrange
        _mockManager.GetStatusAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((ProjectionStatusDto?)null);

        // Act
        var response = await _client.GetAsync(
            "/api/eventstore/admin/projections/NonExistent.Projection",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetProjection_ReturnsProjection_WhenProjectionExists()
    {
        // Arrange
        var expectedStatus = new ProjectionStatusDto(
            ProjectionName: "TestProjection",
            Version: 2,
            State: ProjectionState.Paused,
            Position: 50,
            TotalEvents: 100,
            ProgressPercentage: 50.0,
            LastProcessedAt: DateTimeOffset.UtcNow,
            LastError: null,
            FailedEventSequence: null,
            RebuildStartedAt: null,
            RebuildCompletedAt: null);
        _mockManager.GetStatusAsync("TestProjection", Arg.Any<CancellationToken>())
            .Returns(expectedStatus);

        // Act
        var response = await _client.GetAsync(
            "/api/eventstore/admin/projections/TestProjection",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var projection = await response.Content.ReadFromJsonAsync<ProjectionStatusDto>(
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(projection);
        Assert.Equal("TestProjection", projection.ProjectionName);
        Assert.Equal(ProjectionState.Paused, projection.State);
        Assert.Equal(50, projection.Position);
        Assert.Equal(2, projection.Version);
    }

    #endregion

    #region POST /projections/{name}/rebuild Tests

    [Fact]
    public async Task RebuildProjection_ReturnsAccepted_WhenSuccessful()
    {
        // Arrange
        _mockManager.RebuildAsync("TestProjection", Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Act
        var response = await _client.PostAsync(
            "/api/eventstore/admin/projections/TestProjection/rebuild",
            null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await _mockManager.Received(1).RebuildAsync("TestProjection", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RebuildProjection_ReturnsBadRequest_WhenInvalidOperation()
    {
        // Arrange
        _mockManager.RebuildAsync("TestProjection", Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("Projection is already rebuilding"));

        // Act
        var response = await _client.PostAsync(
            "/api/eventstore/admin/projections/TestProjection/rebuild",
            null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    #endregion

    #region POST /projections/{name}/pause Tests

    [Fact]
    public async Task PauseProjection_ReturnsOk_WhenSuccessful()
    {
        // Arrange
        _mockManager.PauseAsync("TestProjection", Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Act
        var response = await _client.PostAsync(
            "/api/eventstore/admin/projections/TestProjection/pause",
            null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _mockManager.Received(1).PauseAsync("TestProjection", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PauseProjection_ReturnsBadRequest_WhenInvalidOperation()
    {
        // Arrange
        _mockManager.PauseAsync("TestProjection", Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("Projection is already paused"));

        // Act
        var response = await _client.PostAsync(
            "/api/eventstore/admin/projections/TestProjection/pause",
            null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    #endregion

    #region POST /projections/{name}/resume Tests

    [Fact]
    public async Task ResumeProjection_ReturnsOk_WhenSuccessful()
    {
        // Arrange
        _mockManager.ResumeAsync("TestProjection", Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Act
        var response = await _client.PostAsync(
            "/api/eventstore/admin/projections/TestProjection/resume",
            null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _mockManager.Received(1).ResumeAsync("TestProjection", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResumeProjection_ReturnsBadRequest_WhenInvalidOperation()
    {
        // Arrange
        _mockManager.ResumeAsync("TestProjection", Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("Projection is not paused"));

        // Act
        var response = await _client.PostAsync(
            "/api/eventstore/admin/projections/TestProjection/resume",
            null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    #endregion

    #region GET /projections/{name}/failed-event Tests

    [Fact]
    public async Task GetFailedEvent_ReturnsNotFound_WhenNoFailedEvent()
    {
        // Arrange
        _mockManager.GetFailedEventAsync("TestProjection", Arg.Any<CancellationToken>())
            .Returns((FailedEventDto?)null);

        // Act
        var response = await _client.GetAsync(
            "/api/eventstore/admin/projections/TestProjection/failed-event",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetFailedEvent_ReturnsFailedEvent_WhenFaulted()
    {
        // Arrange
        var expectedFailedEvent = new FailedEventDto(
            EventId: Guid.NewGuid(),
            StreamId: Guid.NewGuid(),
            Version: 1,
            Sequence: 42,
            EventType: "TestEvent",
            Data: "{}",
            Timestamp: DateTimeOffset.UtcNow,
            ProjectionError: "Test error message");
        _mockManager.GetFailedEventAsync("TestProjection", Arg.Any<CancellationToken>())
            .Returns(expectedFailedEvent);

        // Act
        var response = await _client.GetAsync(
            "/api/eventstore/admin/projections/TestProjection/failed-event",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var failedEvent = await response.Content.ReadFromJsonAsync<FailedEventDto>(
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(failedEvent);
        Assert.Equal(42, failedEvent.Sequence);
        Assert.Equal("Test error message", failedEvent.ProjectionError);
    }

    #endregion

    #region POST /projections/{name}/retry Tests

    [Fact]
    public async Task RetryFailedEvent_ReturnsOk_WhenSuccessful()
    {
        // Arrange
        _mockManager.RetryFailedEventAsync("TestProjection", Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Act
        var response = await _client.PostAsync(
            "/api/eventstore/admin/projections/TestProjection/retry",
            null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _mockManager.Received(1).RetryFailedEventAsync("TestProjection", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetryFailedEvent_ReturnsBadRequest_WhenInvalidOperation()
    {
        // Arrange
        _mockManager.RetryFailedEventAsync("TestProjection", Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("Projection is not faulted"));

        // Act
        var response = await _client.PostAsync(
            "/api/eventstore/admin/projections/TestProjection/retry",
            null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    #endregion

    #region POST /projections/{name}/skip Tests

    [Fact]
    public async Task SkipFailedEvent_ReturnsOk_WhenSuccessful()
    {
        // Arrange
        _mockManager.SkipFailedEventAsync("TestProjection", Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Act
        var response = await _client.PostAsync(
            "/api/eventstore/admin/projections/TestProjection/skip",
            null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _mockManager.Received(1).SkipFailedEventAsync("TestProjection", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SkipFailedEvent_ReturnsBadRequest_WhenInvalidOperation()
    {
        // Arrange
        _mockManager.SkipFailedEventAsync("TestProjection", Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("Projection is not faulted"));

        // Act
        var response = await _client.PostAsync(
            "/api/eventstore/admin/projections/TestProjection/skip",
            null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    #endregion

    #region Route Configuration Tests

    [Fact]
    public async Task DefaultRoutePrefix_IsApiEventstoreAdmin()
    {
        // This test verifies the default route prefix is "/api/eventstore/admin"
        // Arrange
        _mockManager.GetAllStatusesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ProjectionStatusDto>());

        // Act
        var response = await _client.GetAsync("/api/eventstore/admin/projections",
            TestContext.Current.CancellationToken);

        // Assert - endpoint should exist at the default route
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task NonExistentEndpoint_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/eventstore/admin/nonexistent",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion
}

/// <summary>
/// Tests for custom route prefix configuration.
/// </summary>
public class AdminEndpointOptionsTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private IProjectionManager _mockManager = null!;

    public async ValueTask InitializeAsync()
    {
        _mockManager = Substitute.For<IProjectionManager>();
        _mockManager.GetAllStatusesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ProjectionStatusDto>());

        _host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddSingleton(_mockManager);
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapGroup("/custom/admin")
                                .MapEventStoreAdmin();
                        });
                    });
            })
            .Build();

        await _host.StartAsync();
        _client = _host.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    [Fact]
    public async Task CustomRoutePrefix_WorksCorrectly()
    {
        // Act
        var response = await _client.GetAsync("/custom/admin/projections",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DefaultRoutePrefix_DoesNotWork_WhenCustomPrefixConfigured()
    {
        // Act
        var response = await _client.GetAsync("/api/eventstore/admin/projections",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

/// <summary>
/// Tests for custom authorization configuration.
/// </summary>
public class AdminEndpointAuthorizationTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private IProjectionManager _mockManager = null!;
    private bool _authorizationResult = true;

    public async ValueTask InitializeAsync()
    {
        _mockManager = Substitute.For<IProjectionManager>();
        _mockManager.GetAllStatusesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ProjectionStatusDto>());

        _host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddSingleton(_mockManager);
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            var group = endpoints.MapGroup("/api/eventstore/admin")
                                .MapEventStoreAdmin();
                            
                            // Add custom authorization filter
                            group.AddEndpointFilter(async (context, next) =>
                            {
                                if (!_authorizationResult)
                                {
                                    return Results.Unauthorized();
                                }
                                return await next(context);
                            });
                        });
                    });
            })
            .Build();

        await _host.StartAsync();
        _client = _host.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    [Fact]
    public async Task EndpointFilter_AllowsRequest_WhenAuthorized()
    {
        // Arrange
        _authorizationResult = true;

        // Act
        var response = await _client.GetAsync("/api/eventstore/admin/projections",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task EndpointFilter_ReturnsUnauthorized_WhenNotAuthorized()
    {
        // Arrange
        _authorizationResult = false;

        // Act
        var response = await _client.GetAsync("/api/eventstore/admin/projections",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
