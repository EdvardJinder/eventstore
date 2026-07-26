using System.Runtime.CompilerServices;
using EventStoreCore.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace EventStoreCore;

internal sealed class DbContextEventLogReader(DbContext db) : IEventLogReader
{
    private readonly EventTypeRegistry? _eventTypes = ResolveService<EventTypeRegistry>(db);
    private readonly IEventStoreSerializer _serializer =
        ResolveService<IEventStoreSerializer>(db) ?? new SystemTextJsonEventStoreSerializer();

    public async Task<EventLogPage> ReadPageAsync(
        EventLogReadOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        Validate(options);

        var headSequence = await db.Set<DbEvent>()
            .AsNoTracking()
            .MaxAsync(@event => (long?)@event.Sequence, cancellationToken)
            ?? 0;
        var throughSequence = Math.Min(options.ThroughSequence ?? headSequence, headSequence);
        if (throughSequence <= options.AfterSequence)
        {
            return new EventLogPage([], throughSequence, null);
        }

        var streamTypes = Normalize(options.StreamTypes);
        var eventTypes = Normalize(options.EventTypes);
        IQueryable<DbEvent> query = db.Set<DbEvent>()
            .AsNoTracking()
            .Where(@event =>
                @event.Sequence > options.AfterSequence &&
                @event.Sequence <= throughSequence);

        if (options.TenantId.HasValue)
        {
            query = query.Where(@event => @event.TenantId == options.TenantId.Value);
        }

        if (streamTypes.Length > 0)
        {
            query = query.Where(@event => streamTypes.Contains(@event.StreamType));
        }

        if (eventTypes.Length > 0)
        {
            query = query.Where(@event => eventTypes.Contains(@event.TypeName));
        }

        var records = await query
            .OrderBy(@event => @event.Sequence)
            .Take(options.MaxCount + 1)
            .ToListAsync(cancellationToken);
        var hasMore = records.Count > options.MaxCount;
        if (hasMore)
        {
            records.RemoveAt(records.Count - 1);
        }

        var events = records
            .Select(@event => @event.ToEvent(_eventTypes, _serializer))
            .ToArray();
        return new EventLogPage(
            events,
            throughSequence,
            hasMore ? records[^1].Sequence : null);
    }

    public async IAsyncEnumerable<IEvent> ReadAsync(
        EventLogReadOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        Validate(options);

        var pageOptions = Copy(options);
        long? headSequence = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await ReadPageAsync(pageOptions, cancellationToken);
            headSequence ??= page.HeadSequence;
            foreach (var @event in page.Events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return @event;
            }

            if (!page.NextSequence.HasValue)
            {
                yield break;
            }

            pageOptions.AfterSequence = page.NextSequence.Value;
            pageOptions.ThroughSequence = Math.Min(
                options.ThroughSequence ?? headSequence.Value,
                headSequence.Value);
        }
    }

    private static void Validate(EventLogReadOptions options)
    {
        if (options.AfterSequence < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.AfterSequence,
                "The exclusive lower sequence bound cannot be negative.");
        }

        if (options.ThroughSequence < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.ThroughSequence,
                "The inclusive upper sequence bound cannot be negative.");
        }

        if (options.MaxCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.MaxCount,
                "Page size must be greater than zero.");
        }

        ValidateFilter(options.StreamTypes, nameof(options.StreamTypes));
        ValidateFilter(options.EventTypes, nameof(options.EventTypes));
    }

    private static void ValidateFilter(
        IReadOnlyCollection<string>? values,
        string parameterName)
    {
        if (values?.Any(value => value is null) == true)
        {
            throw new ArgumentException(
                "Filter values cannot contain null.",
                parameterName);
        }
    }

    private static string[] Normalize(IReadOnlyCollection<string>? values) =>
        values?
            .Distinct(StringComparer.Ordinal)
            .ToArray()
        ?? [];

    private static EventLogReadOptions Copy(EventLogReadOptions options) =>
        new()
        {
            AfterSequence = options.AfterSequence,
            ThroughSequence = options.ThroughSequence,
            MaxCount = options.MaxCount,
            TenantId = options.TenantId,
            StreamTypes = Normalize(options.StreamTypes),
            EventTypes = Normalize(options.EventTypes)
        };

    private static TService? ResolveService<TService>(DbContext dbContext)
        where TService : class
    {
        try
        {
            var options = dbContext.GetService<IDbContextOptions>();
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
}
