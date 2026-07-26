using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EventStoreCore;

internal sealed class SequenceCommitOrderInterceptor :
    DbCommandInterceptor,
    ISaveChangesInterceptor
{
    private readonly ConditionalWeakTable<DbContext, SaveState> _states = new();

    public InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        BeginSave(eventData.Context);
        return result;
    }

    public ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        BeginSave(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        EndSave(eventData.Context);
        return result;
    }

    public ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        EndSave(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        EndSave(eventData.Context);
    }

    public Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        EndSave(eventData.Context);
        return Task.CompletedTask;
    }

    public void SaveChangesCanceled(DbContextEventData eventData)
    {
        EndSave(eventData.Context);
    }

    public Task SaveChangesCanceledAsync(
        DbContextEventData eventData,
        CancellationToken cancellationToken = default)
    {
        EndSave(eventData.Context);
        return Task.CompletedTask;
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        AcquireLock(command, eventData);
        return result;
    }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        await AcquireLockAsync(command, eventData, cancellationToken);
        return result;
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        AcquireLock(command, eventData);
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        await AcquireLockAsync(command, eventData, cancellationToken);
        return result;
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result)
    {
        AcquireLock(command, eventData);
        return result;
    }

    public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        await AcquireLockAsync(command, eventData, cancellationToken);
        return result;
    }

    private void BeginSave(DbContext? dbContext)
    {
        if (dbContext is null ||
            dbContext.Model.FindAnnotation(SequenceCommitOrder.AcquireLockSqlAnnotation)?.Value
                is not string)
        {
            return;
        }

        var state = _states.GetValue(dbContext, _ => new SaveState());
        if (state.IsSaving)
        {
            return;
        }

        state.IsSaving = true;
        state.OriginalAutoTransactionBehavior = dbContext.Database.AutoTransactionBehavior;
        state.AcquiredDbTransaction = null;
        state.AcquiredAmbientTransaction = null;
        dbContext.Database.AutoTransactionBehavior = AutoTransactionBehavior.Always;
    }

    private void EndSave(DbContext? dbContext)
    {
        if (dbContext is null || !_states.TryGetValue(dbContext, out var state) || !state.IsSaving)
        {
            return;
        }

        dbContext.Database.AutoTransactionBehavior = state.OriginalAutoTransactionBehavior;
        state.IsSaving = false;
        state.AcquiredDbTransaction = null;
        state.AcquiredAmbientTransaction = null;
    }

    private void AcquireLock(DbCommand command, CommandEventData eventData)
    {
        var acquisition = GetAcquisition(command, eventData);
        if (acquisition is null)
        {
            return;
        }

        using var lockCommand = command.Connection!.CreateCommand();
        lockCommand.CommandText = acquisition.Sql;
        lockCommand.Transaction = command.Transaction;
        lockCommand.CommandTimeout = command.CommandTimeout;
        lockCommand.ExecuteNonQuery();
        acquisition.MarkAcquired();
    }

    private async Task AcquireLockAsync(
        DbCommand command,
        CommandEventData eventData,
        CancellationToken cancellationToken)
    {
        var acquisition = GetAcquisition(command, eventData);
        if (acquisition is null)
        {
            return;
        }

        await using var lockCommand = command.Connection!.CreateCommand();
        lockCommand.CommandText = acquisition.Sql;
        lockCommand.Transaction = command.Transaction;
        lockCommand.CommandTimeout = command.CommandTimeout;
        await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        acquisition.MarkAcquired();
    }

    private LockAcquisition? GetAcquisition(DbCommand command, CommandEventData eventData)
    {
        var dbContext = eventData.Context;
        if (dbContext is null ||
            !_states.TryGetValue(dbContext, out var state) ||
            !state.IsSaving ||
            !RequiresCommitOrderLock(dbContext))
        {
            return null;
        }

        var dbTransaction = command.Transaction;
        var ambientTransaction = Transaction.Current;
        if (dbTransaction is not null && ReferenceEquals(state.AcquiredDbTransaction, dbTransaction))
        {
            return null;
        }

        if (dbTransaction is null &&
            ambientTransaction is not null &&
            ReferenceEquals(state.AcquiredAmbientTransaction, ambientTransaction))
        {
            return null;
        }

        if (dbTransaction is null && ambientTransaction is null)
        {
            if (IsSequenceInsertCommand(dbContext, command.CommandText))
            {
                throw new InvalidOperationException(
                    "EventStoreCore cannot allocate a global sequence without an active database transaction. " +
                    "Do not disable EF Core automatic transactions for event-store or entity-outbox writes.");
            }

            return null;
        }

        var sql = dbContext.Model
            .FindAnnotation(SequenceCommitOrder.AcquireLockSqlAnnotation)!
            .Value as string;
        if (string.IsNullOrWhiteSpace(sql))
        {
            return null;
        }

        return new LockAcquisition(
            sql,
            () =>
            {
                state.AcquiredDbTransaction = dbTransaction;
                state.AcquiredAmbientTransaction = ambientTransaction;
            });
    }

    private static bool RequiresCommitOrderLock(DbContext dbContext)
    {
        return dbContext.ChangeTracker.Entries<DbEvent>()
                .Any(entry => entry.State == EntityState.Added) ||
            dbContext.ChangeTracker.Entries<DbOutboxMessage>()
                .Any(entry => entry.State == EntityState.Added);
    }

    private static bool IsSequenceInsertCommand(DbContext dbContext, string commandText)
    {
        var eventsMarker = dbContext.Model
            .FindAnnotation(SequenceCommitOrder.EventsInsertMarkerAnnotation)
            ?.Value as string;
        var outboxMarker = dbContext.Model
            .FindAnnotation(SequenceCommitOrder.OutboxInsertMarkerAnnotation)
            ?.Value as string;

        return ContainsMarker(commandText, eventsMarker) ||
            ContainsMarker(commandText, outboxMarker);
    }

    private static bool ContainsMarker(string commandText, string? marker)
    {
        return !string.IsNullOrEmpty(marker) &&
            commandText.Contains(marker, StringComparison.Ordinal);
    }

    private sealed class SaveState
    {
        internal bool IsSaving { get; set; }

        internal AutoTransactionBehavior OriginalAutoTransactionBehavior { get; set; }

        internal DbTransaction? AcquiredDbTransaction { get; set; }

        internal Transaction? AcquiredAmbientTransaction { get; set; }
    }

    private sealed record LockAcquisition(string Sql, Action MarkAcquired);
}
