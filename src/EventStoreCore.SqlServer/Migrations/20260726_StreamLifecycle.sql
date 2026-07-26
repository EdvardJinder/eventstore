ALTER TABLE [Streams]
    ADD [LifecycleState] int NOT NULL
        CONSTRAINT [DF_Streams_LifecycleState] DEFAULT 0;

CREATE TABLE [StreamLifecycleEntries] (
    [Id] uniqueidentifier NOT NULL,
    [StreamId] uniqueidentifier NOT NULL,
    [StreamType] nvarchar(450) NOT NULL,
    [TenantId] uniqueidentifier NOT NULL,
    [FromState] int NOT NULL,
    [ToState] int NOT NULL,
    [StreamVersion] bigint NOT NULL,
    [ChangedAtUtc] datetimeoffset NOT NULL,
    [Actor] nvarchar(500) NOT NULL,
    [Reason] nvarchar(2000) NOT NULL,
    [CorrelationId] nvarchar(500) NULL,
    CONSTRAINT [PK_StreamLifecycleEntries] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_StreamLifecycleEntries_Streams_StreamId_StreamType_TenantId]
        FOREIGN KEY ([StreamId], [StreamType], [TenantId])
        REFERENCES [Streams] ([Id], [StreamType], [TenantId])
        ON DELETE CASCADE
);

CREATE INDEX [IX_StreamLifecycleEntries_StreamId_StreamType_TenantId_ChangedAtUtc]
    ON [StreamLifecycleEntries] ([StreamId], [StreamType], [TenantId], [ChangedAtUtc]);

CREATE INDEX [IX_StreamLifecycleEntries_TenantId_ChangedAtUtc]
    ON [StreamLifecycleEntries] ([TenantId], [ChangedAtUtc]);
