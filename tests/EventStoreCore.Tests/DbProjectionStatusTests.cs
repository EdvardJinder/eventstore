using EventStoreCore;
using EventStoreCore.Abstractions;

namespace EventStoreCore.Tests;

/// <summary>
/// Unit tests for the DbProjectionStatus entity and its ToDto() conversion.
/// </summary>
public class DbProjectionStatusTests
{
    [Fact]
    public void ToDto_WithAllPropertiesSet_MapsCorrectly()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var status = new DbProjectionStatus
        {
            ProjectionName = "TestProjection",
            Version = 2,
            State = ProjectionState.Active,
            Position = 100,
            TotalEvents = 200,
            LastProcessedAt = now,
            LastError = null,
            FailedEventSequence = null,
            RebuildStartedAt = now.AddHours(-1),
            RebuildCompletedAt = now.AddMinutes(-30)
        };

        // Act
        var dto = status.ToDto();

        // Assert
        Assert.Equal("TestProjection", dto.ProjectionName);
        Assert.Equal(2, dto.Version);
        Assert.Equal(ProjectionState.Active, dto.State);
        Assert.Equal(100, dto.Position);
        Assert.Equal(200, dto.TotalEvents);
        Assert.Equal(50.0, dto.ProgressPercentage); // 100/200 * 100 = 50%
        Assert.Equal(now, dto.LastProcessedAt);
        Assert.Null(dto.LastError);
        Assert.Null(dto.FailedEventSequence);
        Assert.Equal(now.AddHours(-1), dto.RebuildStartedAt);
        Assert.Equal(now.AddMinutes(-30), dto.RebuildCompletedAt);
    }

    [Fact]
    public void ToDto_WithDefaultValues_MapsCorrectly()
    {
        // Arrange
        var status = new DbProjectionStatus
        {
            ProjectionName = "NewProjection"
        };

        // Act
        var dto = status.ToDto();

        // Assert
        Assert.Equal("NewProjection", dto.ProjectionName);
        Assert.Equal(1, dto.Version); // Default version
        Assert.Equal(ProjectionState.Active, dto.State); // Default state
        Assert.Equal(0, dto.Position); // Default position
        Assert.Null(dto.TotalEvents);
        Assert.Null(dto.ProgressPercentage);
        Assert.Null(dto.LastProcessedAt);
        Assert.Null(dto.LastError);
        Assert.Null(dto.FailedEventSequence);
        Assert.Null(dto.RebuildStartedAt);
        Assert.Null(dto.RebuildCompletedAt);
    }

    [Fact]
    public void ToDto_WhenRebuilding_HasCorrectState()
    {
        // Arrange
        var rebuildStarted = DateTimeOffset.UtcNow;
        var status = new DbProjectionStatus
        {
            ProjectionName = "RebuildingProjection",
            State = ProjectionState.Rebuilding,
            Position = 50,
            TotalEvents = 100,
            RebuildStartedAt = rebuildStarted
        };

        // Act
        var dto = status.ToDto();

        // Assert
        Assert.Equal(ProjectionState.Rebuilding, dto.State);
        Assert.Equal(50.0, dto.ProgressPercentage); // 50/100 * 100 = 50%
        Assert.Equal(rebuildStarted, dto.RebuildStartedAt);
        Assert.Null(dto.RebuildCompletedAt);
    }

    [Fact]
    public void ToDto_WhenFaulted_IncludesErrorInfo()
    {
        // Arrange
        var status = new DbProjectionStatus
        {
            ProjectionName = "FaultedProjection",
            State = ProjectionState.Faulted,
            Position = 42,
            LastError = "NullReferenceException: Object reference not set",
            FailedEventSequence = 43
        };

        // Act
        var dto = status.ToDto();

        // Assert
        Assert.Equal(ProjectionState.Faulted, dto.State);
        Assert.Equal("NullReferenceException: Object reference not set", dto.LastError);
        Assert.Equal(43, dto.FailedEventSequence);
        Assert.Equal(42, dto.Position);
    }

    [Fact]
    public void ToDto_WhenPaused_HasCorrectState()
    {
        // Arrange
        var status = new DbProjectionStatus
        {
            ProjectionName = "PausedProjection",
            State = ProjectionState.Paused,
            Position = 75
        };

        // Act
        var dto = status.ToDto();

        // Assert
        Assert.Equal(ProjectionState.Paused, dto.State);
        Assert.Equal(75, dto.Position);
    }

    [Fact]
    public void ToDto_ProgressPercentage_CalculatesCorrectly_WhenTotalEventsIsZero()
    {
        // Arrange - edge case where TotalEvents is 0
        var status = new DbProjectionStatus
        {
            ProjectionName = "EmptyProjection",
            Position = 0,
            TotalEvents = 0
        };

        // Act
        var dto = status.ToDto();

        // Assert - should return null progress to avoid division by zero
        Assert.Null(dto.ProgressPercentage);
    }

    [Fact]
    public void ToDto_ProgressPercentage_CalculatesCorrectly_WhenPositionExceedsTotalEvents()
    {
        // Arrange - edge case where Position > TotalEvents (can happen if events added during rebuild)
        var status = new DbProjectionStatus
        {
            ProjectionName = "OverflowProjection",
            Position = 150,
            TotalEvents = 100
        };

        // Act
        var dto = status.ToDto();

        // Assert - progress can exceed 100%
        Assert.Equal(150.0, dto.ProgressPercentage);
    }

    [Fact]
    public void ToDto_ProgressPercentage_RoundsToTwoDecimalPlaces()
    {
        // Arrange - position that results in repeating decimal
        var status = new DbProjectionStatus
        {
            ProjectionName = "PrecisionProjection",
            Position = 1,
            TotalEvents = 3 // 1/3 = 33.333...%
        };

        // Act
        var dto = status.ToDto();

        // Assert - should be rounded to 2 decimal places
        Assert.Equal(33.33, dto.ProgressPercentage);
    }

    [Fact]
    public void ToDto_ProgressPercentage_ReturnsNull_WhenTotalEventsIsNull()
    {
        // Arrange
        var status = new DbProjectionStatus
        {
            ProjectionName = "NoTotalProjection",
            Position = 50,
            TotalEvents = null
        };

        // Act
        var dto = status.ToDto();

        // Assert
        Assert.Null(dto.ProgressPercentage);
    }

    [Theory]
    [InlineData(ProjectionState.Active)]
    [InlineData(ProjectionState.Rebuilding)]
    [InlineData(ProjectionState.Paused)]
    [InlineData(ProjectionState.Faulted)]
    public void ToDto_PreservesAllProjectionStates(ProjectionState state)
    {
        // Arrange
        var status = new DbProjectionStatus
        {
            ProjectionName = "StateTestProjection",
            State = state
        };

        // Act
        var dto = status.ToDto();

        // Assert
        Assert.Equal(state, dto.State);
    }
}
