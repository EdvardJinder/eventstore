using EventStoreCore.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace EventStoreCore;

/// <summary>
/// EF Core-backed implementation of <see cref="IEventStore" />.
/// </summary>
/// <param name="db">The DbContext used for persistence.</param>
internal sealed class DbContextEventStore(DbContext db) : IEventStore
{
    private readonly SnapshotRegistry? _snapshots = ResolveSnapshotRegistry(db);
    private readonly EventTypeRegistry? _eventTypes = ResolveService<EventTypeRegistry>(db);
    private readonly IEventStoreSerializer _serializer =
        ResolveService<IEventStoreSerializer>(db) ?? new SystemTextJsonEventStoreSerializer();

    /// <inheritdoc />
    public Task<AppendResult> AppendAsync(
        AppendOperation operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(operation.StreamType);

        return AppendCoreAsync(
            operation.StreamType,
            operation.StreamId,
            operation.TenantId,
            operation.ExpectedVersion,
            operation.Events,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<StreamPage?> ReadPageAsync(
        string streamType,
        Guid streamId,
        Guid tenantId,
        StreamReadOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(streamType);
        ArgumentNullException.ThrowIfNull(options);
        ValidateReadOptions(options);

        var capturedVersion = await db.Set<DbStream>()
            .AsNoTracking()
            .Where(x => x.Id == streamId && x.StreamType == streamType && x.TenantId == tenantId)
            .Select(x => (long?)x.CurrentVersion)
            .SingleOrDefaultAsync(cancellationToken);
        if (!capturedVersion.HasValue)
        {
            return null;
        }

        var fromVersion = options.FromVersion
            ?? (options.Direction == StreamReadDirection.Forward ? 1 : capturedVersion.Value);
        var toVersion = options.ToVersion
            ?? (options.Direction == StreamReadDirection.Forward ? capturedVersion.Value : 1);
        var empty = fromVersion < 1
            || toVersion < 1
            || (options.Direction == StreamReadDirection.Forward
                ? fromVersion > toVersion
                : fromVersion < toVersion);
        if (empty)
        {
            return new StreamPage([], capturedVersion.Value, null);
        }

        IQueryable<DbEvent> query = db.Set<DbEvent>()
            .AsNoTracking()
            .Where(x => x.StreamId == streamId
                && x.StreamType == streamType
                && x.TenantId == tenantId
                && x.Version <= capturedVersion.Value);
        query = options.Direction == StreamReadDirection.Forward
            ? query
                .Where(x => x.Version >= fromVersion && x.Version <= toVersion)
                .OrderBy(x => x.Version)
            : query
                .Where(x => x.Version <= fromVersion && x.Version >= toVersion)
                .OrderByDescending(x => x.Version);

        var records = await query.Take(options.MaxCount + 1).ToListAsync(cancellationToken);
        var hasMore = records.Count > options.MaxCount;
        if (hasMore)
        {
            records.RemoveAt(records.Count - 1);
        }

        var events = records.Select(x => x.ToEvent(_eventTypes, _serializer)).ToArray();
        var nextVersion = hasMore && records.Count > 0
            ? records[^1].Version + (options.Direction == StreamReadDirection.Forward ? 1 : -1)
            : (long?)null;
        return new StreamPage(events, capturedVersion.Value, nextVersion);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<IEvent> ReadAsync(
        string streamType,
        Guid streamId,
        Guid tenantId,
        StreamReadOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateReadOptions(options);
        var pageOptions = new StreamReadOptions
        {
            Direction = options.Direction,
            FromVersion = options.FromVersion,
            ToVersion = options.ToVersion,
            MaxCount = options.MaxCount
        };
        long? capturedVersion = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await ReadPageAsync(
                streamType,
                streamId,
                tenantId,
                pageOptions,
                cancellationToken);
            if (page is null)
            {
                yield break;
            }

            capturedVersion ??= page.StreamVersion;
            foreach (var @event in page.Events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return @event;
            }

            if (!page.NextVersion.HasValue)
            {
                yield break;
            }

            pageOptions.FromVersion = page.NextVersion;
            if (options.Direction == StreamReadDirection.Forward)
            {
                pageOptions.ToVersion = Math.Min(
                    options.ToVersion ?? capturedVersion.Value,
                    capturedVersion.Value);
            }
        }
    }

    private static void ValidateReadOptions(StreamReadOptions options)
    {
        if (options.MaxCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.MaxCount,
                "Page size must be greater than zero.");
        }

        if (!Enum.IsDefined(options.Direction))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.Direction,
                "Read direction is not supported.");
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyStream> AppendAsync(
        Guid streamId,
        ExpectedVersion expectedVersion,
        IEnumerable<object> events,
        CancellationToken cancellationToken = default)
        => AppendAsync(string.Empty, streamId, Guid.Empty, expectedVersion, events, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyStream> AppendAsync(
        Guid streamId,
        Guid tenantId,
        ExpectedVersion expectedVersion,
        IEnumerable<object> events,
        CancellationToken cancellationToken = default)
        => AppendAsync(string.Empty, streamId, tenantId, expectedVersion, events, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyStream> AppendAsync(
        string streamType,
        Guid streamId,
        ExpectedVersion expectedVersion,
        IEnumerable<object> events,
        CancellationToken cancellationToken = default)
        => AppendAsync(streamType, streamId, Guid.Empty, expectedVersion, events, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyStream> AppendAsync(
        string streamType,
        Guid streamId,
        Guid tenantId,
        ExpectedVersion expectedVersion,
        IEnumerable<object> events,
        CancellationToken cancellationToken = default)
    {
        await AppendCoreAsync(
            streamType,
            streamId,
            tenantId,
            expectedVersion,
            events,
            cancellationToken);

        var committedStream = await db.Set<DbStream>()
            .Where(x => x.Id == streamId && x.StreamType == streamType && x.TenantId == tenantId)
            .Include(x => x.Events)
            .SingleAsync(cancellationToken);

        return new DbContextStream(committedStream, db);
    }

    private async Task<AppendResult> AppendCoreAsync(
        string streamType,
        Guid streamId,
        Guid tenantId,
        ExpectedVersion expectedVersion,
        IEnumerable<object> events,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(streamType);
        ArgumentNullException.ThrowIfNull(events);

        var eventList = events.ToArray();
        ValidateEventPayloads(eventList, nameof(events));
        var hasCallerEventIds = eventList
            .OfType<EventToAppend>()
            .Any(append => append.EventId.HasValue);
        var preparedEvents = hasCallerEventIds ? PrepareEvents(eventList) : [];
        if (hasCallerEventIds)
        {
            ValidateEventIds(preparedEvents);
        }

        var pendingChanges = CapturePendingChanges();
        ValidateNoPendingTargetChanges(
            pendingChanges,
            streamType,
            streamId,
            tenantId);

        var eventRetry = await TryResolveEventIdRetryAsync(
            streamType,
            streamId,
            tenantId,
            expectedVersion,
            preparedEvents,
            cancellationToken);
        if (eventRetry is not null)
        {
            return eventRetry;
        }

        var stream = await db.Set<DbStream>()
            .Where(x => x.TenantId == tenantId && x.StreamType == streamType)
            .Include(x => x.Events)
            .FirstOrDefaultAsync(x => x.Id == streamId, cancellationToken);

        stream = expectedVersion.Mode switch
        {
            ExpectedVersionMode.Any => stream ?? CreateStream(streamType, streamId, tenantId),
            ExpectedVersionMode.NoStream when stream is null => CreateStream(streamType, streamId, tenantId),
            ExpectedVersionMode.NoStream => throw CreateConcurrencyException(
                streamType,
                streamId,
                tenantId,
                expectedVersion,
                stream!.CurrentVersion,
                "Append expected no stream, but the stream already exists."),
            ExpectedVersionMode.StreamExists when stream is not null => stream,
            ExpectedVersionMode.StreamExists => throw CreateConcurrencyException(
                streamType,
                streamId,
                tenantId,
                expectedVersion,
                actualVersion: null,
                "Append expected an existing stream, but the stream was not found."),
            ExpectedVersionMode.Exact when stream is not null && stream.CurrentVersion == expectedVersion.Version => stream,
            ExpectedVersionMode.Exact => throw CreateConcurrencyException(
                streamType,
                streamId,
                tenantId,
                expectedVersion,
                stream?.CurrentVersion,
                $"Append expected stream version {expectedVersion.Version}, but observed {(stream is null ? "no stream" : stream.CurrentVersion.ToString())}."),
            _ => throw new InvalidOperationException($"Unsupported expected-version mode: {expectedVersion.Mode}")
        };

        var previousVersion = stream.CurrentVersion;
        new DbContextStream(stream, db).Append(eventList);
        var appendEntries = CaptureAppendEntries(pendingChanges);
        SuppressUnrelatedChanges(pendingChanges);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return CreateResult(
                streamType,
                streamId,
                tenantId,
                previousVersion,
                stream.CurrentVersion,
                stream.Events
                    .Where(@event => @event.Version > previousVersion)
                    .OrderBy(@event => @event.Version),
                wasAlreadyCommitted: false,
                expectedEventCount: eventList.Length);
        }
        catch (DbUpdateException ex) when (IsEventStoreWriteConflict(ex))
        {
            DetachAppendEntries(appendEntries);

            eventRetry = await TryResolveEventIdRetryAsync(
                streamType,
                streamId,
                tenantId,
                expectedVersion,
                preparedEvents,
                cancellationToken,
                ex);
            if (eventRetry is not null)
            {
                return eventRetry;
            }

            var actualVersion = await GetActualVersionAsync(streamType, streamId, tenantId, cancellationToken);

            throw CreateConcurrencyException(
                streamType,
                streamId,
                tenantId,
                expectedVersion,
                actualVersion,
                "Append failed because another writer modified the stream concurrently.",
                ex);
        }
        finally
        {
            RestoreUnrelatedChanges(pendingChanges);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyStream?> FetchForReadingAsync(Guid streamId, CancellationToken cancellationToken = default)
        => FetchForReadingAsync(string.Empty, streamId, Guid.Empty, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyStream?> FetchForReadingAsync(Guid streamId, Guid tenantId, CancellationToken cancellationToken = default)
        => FetchForReadingAsync(string.Empty, streamId, tenantId, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyStream?> FetchForReadingAsync(string streamType, Guid streamId, CancellationToken cancellationToken = default)
        => FetchForReadingAsync(streamType, streamId, Guid.Empty, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyStream?> FetchForReadingAsync(string streamType, Guid streamId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        var stream = await db.Set<DbStream>()
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.StreamType == streamType)
            .Include(x => x.Events)
            .FirstOrDefaultAsync(x => x.Id == streamId, cancellationToken);
        if (stream is null) return null;
        return new DbContextStream(stream, db);
    }

    /// <inheritdoc />
    public Task<IReadOnlyStream<T>?> FetchForReadingAsync<T>(Guid streamId, CancellationToken cancellationToken = default) where T : IState, new()
        => FetchForReadingAsync<T>(string.Empty, streamId, Guid.Empty, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyStream<T>?> FetchForReadingAsync<T>(Guid streamId, Guid tenantId, CancellationToken cancellationToken = default) where T : IState, new()
        => FetchForReadingAsync<T>(string.Empty, streamId, tenantId, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyStream<T>?> FetchForReadingAsync<T>(string streamType, Guid streamId, CancellationToken cancellationToken = default) where T : IState, new()
        => FetchForReadingAsync<T>(streamType, streamId, Guid.Empty, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyStream<T>?> FetchForReadingAsync<T>(string streamType, Guid streamId, Guid tenantId, CancellationToken cancellationToken = default) where T : IState, new()
    {
        var snapshot = _snapshots?.LoadSnapshot<T>(db, streamType, streamId, tenantId);
        var snapshotVersion = snapshot?.Version ?? 0;

        var stream = await db.Set<DbStream>()
         .AsNoTracking()
         .Where(x => x.TenantId == tenantId && x.StreamType == streamType)
         .Include(x => x.Events.Where(e => e.Version > snapshotVersion))
         .FirstOrDefaultAsync(x => x.Id == streamId, cancellationToken);
        if (stream is null) return null;
        return new DbContextStream<T>(stream, db, DeserializeSnapshot<T>(snapshot));
    }

    /// <inheritdoc />
    public Task<IReadOnlyStream?> FetchForReadingAsync(Guid streamId, long version, CancellationToken cancellationToken = default)
        => FetchForReadingAsync(string.Empty, streamId, Guid.Empty, version, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyStream?> FetchForReadingAsync(Guid streamId, Guid tenantId, long version, CancellationToken cancellationToken = default)
        => FetchForReadingAsync(string.Empty, streamId, tenantId, version, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyStream?> FetchForReadingAsync(string streamType, Guid streamId, long version, CancellationToken cancellationToken = default)
        => FetchForReadingAsync(streamType, streamId, Guid.Empty, version, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyStream?> FetchForReadingAsync(string streamType, Guid streamId, Guid tenantId, long version, CancellationToken cancellationToken = default)
    {
        var stream = await db.Set<DbStream>()
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.StreamType == streamType)
            .Include(x => x.Events.Where(x => x.Version <= version))
            .FirstOrDefaultAsync(x => x.Id == streamId, cancellationToken);
        if (stream is null) return null;
        stream.CurrentVersion = Math.Min(version, stream.CurrentVersion);
        return new DbContextStream(stream);
    }

    /// <inheritdoc />
    public Task<IReadOnlyStream<T>?> FetchForReadingAsync<T>(Guid streamId, long version, CancellationToken cancellationToken = default) where T : IState, new()
        => FetchForReadingAsync<T>(string.Empty, streamId, Guid.Empty, version, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyStream<T>?> FetchForReadingAsync<T>(Guid streamId, Guid tenantId, long version, CancellationToken cancellationToken = default) where T : IState, new()
        => FetchForReadingAsync<T>(string.Empty, streamId, tenantId, version, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyStream<T>?> FetchForReadingAsync<T>(string streamType, Guid streamId, long version, CancellationToken cancellationToken = default) where T : IState, new()
        => FetchForReadingAsync<T>(streamType, streamId, Guid.Empty, version, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyStream<T>?> FetchForReadingAsync<T>(string streamType, Guid streamId, Guid tenantId, long version, CancellationToken cancellationToken = default) where T : IState, new()
    {
        var snapshot = _snapshots?.LoadSnapshot<T>(db, streamType, streamId, tenantId);
        if (snapshot?.Version > version)
        {
            snapshot = null;
        }

        var snapshotVersion = snapshot?.Version ?? 0;
        var stream = await db.Set<DbStream>()
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.StreamType == streamType)
            .Include(x => x.Events.Where(x => x.Version > snapshotVersion && x.Version <= version))
            .FirstOrDefaultAsync(x => x.Id == streamId, cancellationToken);
        if (stream is null) return null;
        stream.CurrentVersion = Math.Min(version, stream.CurrentVersion);
        return new DbContextStream<T>(stream, db, DeserializeSnapshot<T>(snapshot));
    }

    /// <inheritdoc />
    public Task<IStream?> FetchForWritingAsync(Guid streamId, CancellationToken cancellationToken = default)
        => FetchForWritingAsync(string.Empty, streamId, Guid.Empty, cancellationToken);

    /// <inheritdoc />
    public Task<IStream?> FetchForWritingAsync(Guid streamId, Guid tenantId, CancellationToken cancellationToken = default)
        => FetchForWritingAsync(string.Empty, streamId, tenantId, cancellationToken);

    /// <inheritdoc />
    public Task<IStream?> FetchForWritingAsync(string streamType, Guid streamId, CancellationToken cancellationToken = default)
        => FetchForWritingAsync(streamType, streamId, Guid.Empty, cancellationToken);

    /// <inheritdoc />
    public async Task<IStream?> FetchForWritingAsync(string streamType, Guid streamId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        var stream = await db.Set<DbStream>()
            .Where(x => x.TenantId == tenantId && x.StreamType == streamType)
            .Include(x => x.Events)
            .FirstOrDefaultAsync(x => x.Id == streamId, cancellationToken);
        if (stream is null) return null;
        return new DbContextStream(stream, db);
    }

    /// <inheritdoc />
    public Task<IStream<T>?> FetchForWritingAsync<T>(Guid streamId, CancellationToken cancellationToken = default) where T : IState, new()
        => FetchForWritingAsync<T>(string.Empty, streamId, Guid.Empty, cancellationToken);

    /// <inheritdoc />
    public Task<IStream<T>?> FetchForWritingAsync<T>(Guid streamId, Guid tenantId, CancellationToken cancellationToken = default) where T : IState, new()
        => FetchForWritingAsync<T>(string.Empty, streamId, tenantId, cancellationToken);

    /// <inheritdoc />
    public Task<IStream<T>?> FetchForWritingAsync<T>(string streamType, Guid streamId, CancellationToken cancellationToken = default) where T : IState, new()
        => FetchForWritingAsync<T>(streamType, streamId, Guid.Empty, cancellationToken);

    /// <inheritdoc />
    public async Task<IStream<T>?> FetchForWritingAsync<T>(string streamType, Guid streamId, Guid tenantId, CancellationToken cancellationToken = default) where T : IState, new()
    {
        var stream = await db.Set<DbStream>()
          .Where(x => x.TenantId == tenantId && x.StreamType == streamType)
          .Include(x => x.Events)
          .FirstOrDefaultAsync(x => x.Id == streamId, cancellationToken);
        if (stream is null) return null;
        return new DbContextStream<T>(stream, db);
    }

    /// <inheritdoc />
    public IStream StartStream(Guid streamId, params IEnumerable<object> events)
        => StartStream(string.Empty, streamId, Guid.Empty, events);

    /// <inheritdoc />
    public IStream StartStream(Guid streamId, Guid tenantId, params IEnumerable<object> events)
        => StartStream(string.Empty, streamId, tenantId, events);

    /// <inheritdoc />
    public IStream StartStream(string streamType, Guid streamId, params IEnumerable<object> events)
        => StartStream(streamType, streamId, Guid.Empty, events);

    /// <inheritdoc />
    public IStream StartStream(string streamType, Guid streamId, Guid tenantId, params IEnumerable<object> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        var eventList = events.ToArray();
        ValidateEventPayloads(eventList, nameof(events));

        var dbStream = new DbStream
        {
            Id = streamId,
            StreamType = streamType,
            CurrentVersion = 0,
            CreatedTimestamp = DateTime.UtcNow,
            UpdatedTimestamp = DateTime.UtcNow,
            TenantId = tenantId
        };
        db.Add(dbStream);
        var stream = new DbContextStream(dbStream, db);

        stream.Append(eventList);
        return stream;
    }

    /// <inheritdoc />
    public IStream<T> StartStream<T>(Guid streamId, params IEnumerable<object> events) where T : IState, new()
        => StartStream<T>(string.Empty, streamId, Guid.Empty, events);

    /// <inheritdoc />
    public IStream<T> StartStream<T>(Guid streamId, Guid tenantId, params IEnumerable<object> events) where T : IState, new()
        => StartStream<T>(string.Empty, streamId, tenantId, events);

    /// <inheritdoc />
    public IStream<T> StartStream<T>(string streamType, Guid streamId, params IEnumerable<object> events) where T : IState, new()
        => StartStream<T>(streamType, streamId, Guid.Empty, events);

    /// <inheritdoc />
    public IStream<T> StartStream<T>(string streamType, Guid streamId, Guid tenantId, params IEnumerable<object> events) where T : IState, new()
    {
        ArgumentNullException.ThrowIfNull(events);
        var eventList = events.ToArray();
        ValidateEventPayloads(eventList, nameof(events));

        var dbStream = CreateStream(streamType, streamId, tenantId);
        var stream = new DbContextStream<T>(dbStream, db);

        stream.Append(eventList);
        return stream;
    }

    private DbStream CreateStream(string streamType, Guid streamId, Guid tenantId)
    {
        var dbStream = new DbStream
        {
            Id = streamId,
            StreamType = streamType,
            CurrentVersion = 0,
            CreatedTimestamp = DateTime.UtcNow,
            UpdatedTimestamp = DateTime.UtcNow,
            TenantId = tenantId
        };

        db.Add(dbStream);
        return dbStream;
    }

    private PreparedEvent[] PrepareEvents(IEnumerable<object> events)
    {
        return events.Select(@event =>
        {
            var append = @event as EventToAppend;
            var eventData = append?.Data ?? @event;
            var metadata = append?.Metadata;
            var eventType = eventData.GetType();

            return new PreparedEvent(
                append?.EventId,
                _eventTypes?.ResolveName(eventType) ?? EventTypeNameHelper.ToSnakeCase(eventType),
                _serializer.Serialize(eventData, eventType),
                metadata?.CorrelationId,
                metadata?.CausationId,
                metadata?.Actor,
                EventHeaders.Serialize(metadata?.Headers ?? new Dictionary<string, string>()),
                _eventTypes?.ResolveSchemaVersion(eventType) ?? 1);
        }).ToArray();
    }

    private static void ValidateEventIds(IEnumerable<PreparedEvent> events)
    {
        var eventList = events.ToArray();
        if (eventList.Any(@event => @event.EventId.HasValue)
            && eventList.Any(@event => !@event.EventId.HasValue))
        {
            throw new ArgumentException(
                "Every event in a retryable append must have a caller-supplied identifier.",
                nameof(events));
        }

        var eventIds = new HashSet<Guid>();
        foreach (var @event in eventList)
        {
            if (@event.EventId == Guid.Empty)
            {
                throw new ArgumentException("Event identifiers cannot be empty.", nameof(events));
            }

            if (@event.EventId.HasValue && !eventIds.Add(@event.EventId.Value))
            {
                throw new ArgumentException(
                    $"Event identifier '{@event.EventId}' is repeated within the append batch.",
                    nameof(events));
            }
        }
    }

    private async Task<AppendResult?> TryResolveEventIdRetryAsync(
        string streamType,
        Guid streamId,
        Guid tenantId,
        ExpectedVersion expectedVersion,
        IReadOnlyList<PreparedEvent> preparedEvents,
        CancellationToken cancellationToken,
        Exception? innerException = null)
    {
        var suppliedIds = preparedEvents
            .Where(@event => @event.EventId.HasValue)
            .Select(@event => @event.EventId!.Value)
            .ToArray();
        if (suppliedIds.Length == 0)
        {
            return null;
        }

        var storedEvents = await db.Set<DbEvent>()
            .AsNoTracking()
            .Where(@event => suppliedIds.Contains(@event.EventId))
            .ToListAsync(cancellationToken);
        if (storedEvents.Count == 0)
        {
            return null;
        }

        var firstConflictingId = storedEvents[0].EventId;
        if (suppliedIds.Length != preparedEvents.Count || storedEvents.Count != preparedEvents.Count)
        {
            throw CreateEventIdConflict(firstConflictingId, innerException);
        }

        var storedById = storedEvents.ToDictionary(@event => @event.EventId);
        var orderedStoredEvents = new DbEvent[preparedEvents.Count];
        for (var index = 0; index < preparedEvents.Count; index++)
        {
            var prepared = preparedEvents[index];
            if (!prepared.EventId.HasValue
                || !storedById.TryGetValue(prepared.EventId.Value, out var stored)
                || stored.StreamId != streamId
                || stored.StreamType != streamType
                || stored.TenantId != tenantId
                || !Matches(prepared, stored))
            {
                throw CreateEventIdConflict(prepared.EventId ?? firstConflictingId, innerException);
            }

            orderedStoredEvents[index] = stored;
        }

        var firstVersion = orderedStoredEvents[0].Version;
        for (var index = 0; index < orderedStoredEvents.Length; index++)
        {
            if (orderedStoredEvents[index].Version != firstVersion + index)
            {
                throw CreateEventIdConflict(orderedStoredEvents[index].EventId, innerException);
            }
        }

        var previousVersion = firstVersion - 1;
        if ((expectedVersion.Mode == ExpectedVersionMode.NoStream && previousVersion != 0)
            || (expectedVersion.Mode == ExpectedVersionMode.Exact
                && previousVersion != expectedVersion.Version))
        {
            throw CreateEventIdConflict(firstConflictingId, innerException);
        }

        return new AppendResult(
            streamId,
            streamType,
            tenantId,
            previousVersion,
            orderedStoredEvents[^1].Version,
            orderedStoredEvents
                .Select(@event => new AppendedEventInfo(@event.EventId, @event.Version, @event.Sequence))
                .ToArray(),
            wasAlreadyCommitted: true);
    }

    private static AppendResult CreateResult(
        string streamType,
        Guid streamId,
        Guid tenantId,
        long previousVersion,
        long currentVersion,
        IEnumerable<DbEvent> committedEvents,
        bool wasAlreadyCommitted,
        int expectedEventCount)
    {
        var events = committedEvents
            .Select(@event => new AppendedEventInfo(
                @event.EventId,
                @event.Version,
                @event.Sequence))
            .ToArray();

        if (events.Length != expectedEventCount
            || events.Length != currentVersion - previousVersion)
        {
            throw new InvalidOperationException(
                $"The committed append result for stream '{streamType}/{streamId}' is incomplete.");
        }

        return new AppendResult(
            streamId,
            streamType,
            tenantId,
            previousVersion,
            currentVersion,
            events,
            wasAlreadyCommitted);
    }

    private static bool Matches(PreparedEvent prepared, DbEvent stored)
    {
        return string.Equals(prepared.TypeName, stored.TypeName, StringComparison.Ordinal)
            && SerializedValuesEqual(prepared.Data, stored.Data)
            && prepared.CorrelationId == stored.CorrelationId
            && prepared.CausationId == stored.CausationId
            && string.Equals(prepared.Actor, stored.Actor, StringComparison.Ordinal)
            && SerializedValuesEqual(prepared.Headers, stored.Headers)
            && prepared.SchemaVersion == stored.SchemaVersion;
    }

    private static bool SerializedValuesEqual(string left, string right)
    {
        if (string.Equals(left, right, StringComparison.Ordinal))
        {
            return true;
        }

        try
        {
            using var leftDocument = JsonDocument.Parse(left);
            using var rightDocument = JsonDocument.Parse(right);
            return JsonElement.DeepEquals(leftDocument.RootElement, rightDocument.RootElement);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static EventStoreIdempotencyConflictException CreateEventIdConflict(
        Guid eventId,
        Exception? innerException)
    {
        return new EventStoreIdempotencyConflictException(
            $"Event identifier '{eventId}' was already committed as part of a different append.",
            eventId,
            innerException);
    }

    private void DetachAppendEntries(IEnumerable<object> appendEntries)
    {
        foreach (var entity in appendEntries)
        {
            db.Entry(entity).State = EntityState.Detached;
        }
    }

    private static bool IsEventStoreWriteConflict(DbUpdateException exception)
    {
        return exception.Entries.Any(
                entry => entry.Entity is DbEvent or DbStream or DbSnapshot)
            && IsUniqueConstraintViolation(exception);
    }

    private async Task<long?> GetActualVersionAsync(string streamType, Guid streamId, Guid tenantId, CancellationToken cancellationToken)
    {
        return await db.Set<DbStream>()
            .AsNoTracking()
            .Where(stream => stream.Id == streamId && stream.StreamType == streamType && stream.TenantId == tenantId)
            .Select(stream => (long?)stream.CurrentVersion)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private IReadOnlyList<SuppressedEntry> CapturePendingChanges() =>
        db.ChangeTracker.Entries()
            .Where(entry => IsPending(entry.State))
            .Select(entry => new SuppressedEntry(
                entry.Entity,
                entry.State,
                entry.CurrentValues.Clone(),
                entry.OriginalValues.Clone()))
            .ToArray();

    private IReadOnlyList<object> CaptureAppendEntries(
        IReadOnlyList<SuppressedEntry> pendingChanges)
    {
        var pendingEntities = pendingChanges
            .Select(change => change.Entity)
            .ToHashSet(ReferenceEqualityComparer.Instance);
        return db.ChangeTracker.Entries()
            .Where(entry =>
                IsPending(entry.State)
                && !pendingEntities.Contains(entry.Entity))
            .Select(entry => entry.Entity)
            .ToArray();
    }

    private static void ValidateNoPendingTargetChanges(
        IEnumerable<SuppressedEntry> pendingChanges,
        string streamType,
        Guid streamId,
        Guid tenantId)
    {
        var hasPendingTargetChanges = pendingChanges.Any(change =>
            change.Entity switch
            {
                DbStream stream => stream.Id == streamId
                    && stream.StreamType == streamType
                    && stream.TenantId == tenantId,
                DbEvent @event => @event.StreamId == streamId
                    && @event.StreamType == streamType
                    && @event.TenantId == tenantId,
                DbSnapshot snapshot => snapshot.StreamId == streamId
                    && snapshot.StreamType == streamType
                    && snapshot.TenantId == tenantId,
                _ => false
            });
        if (hasPendingTargetChanges)
        {
            throw new InvalidOperationException(
                "Persist pending changes for the target stream before starting an append operation.");
        }
    }

    private void SuppressUnrelatedChanges(IEnumerable<SuppressedEntry> entries)
    {
        foreach (var entry in entries)
        {
            db.Entry(entry.Entity).State = entry.State == EntityState.Added
                ? EntityState.Detached
                : EntityState.Unchanged;
        }
    }

    private void RestoreUnrelatedChanges(IEnumerable<SuppressedEntry> entries)
    {
        foreach (var entry in entries)
        {
            var trackedEntry = db.Entry(entry.Entity);
            if (entry.State != EntityState.Added)
            {
                trackedEntry.CurrentValues.SetValues(entry.CurrentValues);
                trackedEntry.OriginalValues.SetValues(entry.OriginalValues);
            }
            trackedEntry.State = entry.State;
        }
    }

    private static bool IsPending(EntityState state) =>
        state is EntityState.Added or EntityState.Modified or EntityState.Deleted;

    private sealed record SuppressedEntry(
        object Entity,
        EntityState State,
        PropertyValues CurrentValues,
        PropertyValues OriginalValues);

    private static void ValidateEventPayloads(IEnumerable<object> events, string parameterName)
    {
        foreach (var @event in events)
        {
            ArgumentNullException.ThrowIfNull(@event, parameterName);
            var append = @event as EventToAppend;
            if (append?.EventId == Guid.Empty)
            {
                throw new ArgumentException("Event identifiers cannot be empty.", parameterName);
            }

            var eventData = append?.Data ?? @event;
            ArgumentNullException.ThrowIfNull(eventData, parameterName);
            if (append is not null)
            {
                ArgumentNullException.ThrowIfNull(append.Metadata, parameterName);
            }

            var eventType = eventData.GetType();
            if (eventType.IsValueType)
            {
                throw new ArgumentException(
                    $"Event payload type '{eventType.FullName}' is a value type. Event payloads must be reference types.",
                    parameterName);
            }
        }
    }

    private static bool IsUniqueConstraintViolation(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (GetStringProperty(current, "SqlState") == "23505")
            {
                return true;
            }

            if (GetIntProperty(current, "Number") is 2601 or 2627)
            {
                return true;
            }

            if (GetIntProperty(current, "SqliteErrorCode") == 19)
            {
                return true;
            }
        }

        return false;
    }

    private static string? GetStringProperty(Exception exception, string propertyName)
    {
        return exception.GetType().GetProperty(propertyName)?.GetValue(exception) as string;
    }

    private static int? GetIntProperty(Exception exception, string propertyName)
    {
        return exception.GetType().GetProperty(propertyName)?.GetValue(exception) as int?;
    }

    private static EventStoreConcurrencyException CreateConcurrencyException(
        string streamType,
        Guid streamId,
        Guid tenantId,
        ExpectedVersion expectedVersion,
        long? actualVersion,
        string message,
        Exception? innerException = null)
    {
        return new EventStoreConcurrencyException(
            streamType,
            streamId,
            tenantId,
            expectedVersion,
            actualVersion,
            message,
            innerException);
    }

    private static SnapshotRegistry? ResolveSnapshotRegistry(DbContext db)
    {
        try
        {
            var options = db.GetService<IDbContextOptions>();
            var appProvider = options.Extensions
                .OfType<CoreOptionsExtension>()
                .FirstOrDefault()
                ?.ApplicationServiceProvider;

            return appProvider?.GetService<SnapshotRegistry>();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static TService? ResolveService<TService>(DbContext db)
        where TService : class
    {
        try
        {
            var options = db.GetService<IDbContextOptions>();
            var appProvider = options.Extensions
                .OfType<CoreOptionsExtension>()
                .FirstOrDefault()
                ?.ApplicationServiceProvider;

            return appProvider?.GetService<TService>();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private T? DeserializeSnapshot<T>(DbSnapshot? snapshot)
        where T : IState, new()
        => snapshot is null
            ? default
            : (T?)_serializer.Deserialize(snapshot.Data, typeof(T));

    private sealed record PreparedEvent(
        Guid? EventId,
        string TypeName,
        string Data,
        Guid? CorrelationId,
        Guid? CausationId,
        string? Actor,
        string Headers,
        int SchemaVersion);
}
