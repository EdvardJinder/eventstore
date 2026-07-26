ALTER TABLE "Streams"
    ADD COLUMN "LifecycleState" integer NOT NULL DEFAULT 0;

CREATE TABLE "StreamLifecycleEntries" (
    "Id" uuid NOT NULL,
    "StreamId" uuid NOT NULL,
    "StreamType" text NOT NULL,
    "TenantId" uuid NOT NULL,
    "FromState" integer NOT NULL,
    "ToState" integer NOT NULL,
    "StreamVersion" bigint NOT NULL,
    "ChangedAtUtc" timestamp with time zone NOT NULL,
    "Actor" character varying(500) NOT NULL,
    "Reason" character varying(2000) NOT NULL,
    "CorrelationId" character varying(500),
    CONSTRAINT "PK_StreamLifecycleEntries" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_StreamLifecycleEntries_Streams_StreamId_StreamType_TenantId"
        FOREIGN KEY ("StreamId", "StreamType", "TenantId")
        REFERENCES "Streams" ("Id", "StreamType", "TenantId")
        ON DELETE CASCADE
);

CREATE INDEX "IX_StreamLifecycleEntries_StreamId_StreamType_TenantId_ChangedAtUtc"
    ON "StreamLifecycleEntries" ("StreamId", "StreamType", "TenantId", "ChangedAtUtc");

CREATE INDEX "IX_StreamLifecycleEntries_TenantId_ChangedAtUtc"
    ON "StreamLifecycleEntries" ("TenantId", "ChangedAtUtc");
