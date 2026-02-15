-- ============================================================================
-- DATABASE MIGRATION SCRIPTS
-- ============================================================================
-- Migration from v1.x (Guid ID + string StreamType) to v2.0 (string ID only)
-- ============================================================================

-- IMPORTANT: Test these scripts on a copy of your production database first!
-- IMPORTANT: Schedule a maintenance window (4-8 hours for large databases)
-- IMPORTANT: Take a full database backup before running

-- ============================================================================
-- PostgreSQL Migration Script
-- ============================================================================

BEGIN TRANSACTION;

-- Step 1: Add new string ID columns (nullable temporarily)
-- ----------------------------------------------------------------
ALTER TABLE "Streams" 
ADD COLUMN "Id_New" VARCHAR(255) DEFAULT NULL;

ALTER TABLE "Events" 
ADD COLUMN "StreamId_New" VARCHAR(255) DEFAULT NULL;

COMMENT ON COLUMN "Streams"."Id_New" IS 'Temporary column for migration to string-based stream IDs';
COMMENT ON COLUMN "Events"."StreamId_New" IS 'Temporary column for migration to string-based stream IDs';


-- Step 2: Populate new columns with converted data
-- ----------------------------------------------------------------
-- Conversion logic: If StreamType is empty, use GUID as-is
--                   Otherwise, prefix GUID with "StreamType:"

UPDATE "Streams" 
SET "Id_New" = CASE 
    WHEN "StreamType" = '' OR "StreamType" IS NULL 
        THEN "Id"::TEXT
    ELSE "StreamType" || ':' || "Id"::TEXT
END;

UPDATE "Events"
SET "StreamId_New" = CASE
    WHEN "StreamType" = '' OR "StreamType" IS NULL
        THEN "StreamId"::TEXT
    ELSE "StreamType" || ':' || "StreamId"::TEXT  
END;


-- Step 3: Validate conversion (CRITICAL - DO NOT SKIP!)
-- ----------------------------------------------------------------

-- Check for NULL values (should return 0)
DO $$
DECLARE
    null_streams INTEGER;
    null_events INTEGER;
BEGIN
    SELECT COUNT(*) INTO null_streams FROM "Streams" WHERE "Id_New" IS NULL;
    SELECT COUNT(*) INTO null_events FROM "Events" WHERE "StreamId_New" IS NULL;
    
    IF null_streams > 0 THEN
        RAISE EXCEPTION 'Found % NULL values in Streams.Id_New - ABORTING', null_streams;
    END IF;
    
    IF null_events > 0 THEN
        RAISE EXCEPTION 'Found % NULL values in Events.StreamId_New - ABORTING', null_events;
    END IF;
    
    RAISE NOTICE 'Validation passed: No NULL values found';
END $$;

-- Check for duplicate stream IDs (after removing StreamType from key)
-- This should NOT happen if StreamType was properly used, but check anyway
DO $$
DECLARE
    duplicate_count INTEGER;
BEGIN
    SELECT COUNT(*) INTO duplicate_count
    FROM (
        SELECT "TenantId", "Id_New", COUNT(*) as cnt
        FROM "Streams"
        GROUP BY "TenantId", "Id_New"
        HAVING COUNT(*) > 1
    ) duplicates;
    
    IF duplicate_count > 0 THEN
        RAISE EXCEPTION 'Found % duplicate stream IDs - ABORTING. Manual intervention required.', duplicate_count;
    END IF;
    
    RAISE NOTICE 'Validation passed: No duplicate stream IDs found';
END $$;

-- Check data integrity: Events reference existing Streams
DO $$
DECLARE
    orphan_count INTEGER;
BEGIN
    SELECT COUNT(*) INTO orphan_count
    FROM "Events" e
    WHERE NOT EXISTS (
        SELECT 1 FROM "Streams" s
        WHERE s."TenantId" = e."TenantId"
          AND s."Id_New" = e."StreamId_New"
    );
    
    IF orphan_count > 0 THEN
        RAISE EXCEPTION 'Found % orphaned events - ABORTING. Data integrity issue!', orphan_count;
    END IF;
    
    RAISE NOTICE 'Validation passed: All events reference existing streams';
END $$;


-- Step 4: Drop old constraints
-- ----------------------------------------------------------------

-- Drop foreign key from Events to Streams
ALTER TABLE "Events" 
DROP CONSTRAINT IF EXISTS "FK_Events_Streams_TenantId_StreamId_StreamType";

ALTER TABLE "Events"
DROP CONSTRAINT IF EXISTS "FK_Events_Streams";

-- Drop primary keys
ALTER TABLE "Streams" 
DROP CONSTRAINT IF EXISTS "PK_Streams";

ALTER TABLE "Events"
DROP CONSTRAINT IF EXISTS "PK_Events";

-- Drop indexes that include old columns
DROP INDEX IF EXISTS "IX_Events_Sequence";
DROP INDEX IF EXISTS "IX_Events_Timestamp";
DROP INDEX IF EXISTS "IX_Streams_TenantId";


-- Step 5: Drop old columns
-- ----------------------------------------------------------------

ALTER TABLE "Streams" 
DROP COLUMN "StreamType";

ALTER TABLE "Events"
DROP COLUMN "StreamType";

ALTER TABLE "Streams"
DROP COLUMN "Id";

ALTER TABLE "Events"
DROP COLUMN "StreamId";


-- Step 6: Rename new columns to final names
-- ----------------------------------------------------------------

ALTER TABLE "Streams" 
RENAME COLUMN "Id_New" TO "Id";

ALTER TABLE "Events"
RENAME COLUMN "StreamId_New" TO "StreamId";


-- Step 7: Make columns NOT NULL
-- ----------------------------------------------------------------

ALTER TABLE "Streams"
ALTER COLUMN "Id" SET NOT NULL;

ALTER TABLE "Events"
ALTER COLUMN "StreamId" SET NOT NULL;


-- Step 8: Recreate primary keys
-- ----------------------------------------------------------------

ALTER TABLE "Streams"
ADD CONSTRAINT "PK_Streams" PRIMARY KEY ("TenantId", "Id");

ALTER TABLE "Events"
ADD CONSTRAINT "PK_Events" PRIMARY KEY ("TenantId", "StreamId", "Version");


-- Step 9: Recreate foreign key
-- ----------------------------------------------------------------

ALTER TABLE "Events"
ADD CONSTRAINT "FK_Events_Streams"
    FOREIGN KEY ("TenantId", "StreamId")
    REFERENCES "Streams" ("TenantId", "Id")
    ON DELETE CASCADE;


-- Step 10: Recreate indexes
-- ----------------------------------------------------------------

-- Global sequence index for reading events across all streams
CREATE INDEX "IX_Events_Sequence" 
ON "Events" ("Sequence" ASC);

-- Timestamp index for time-based queries
CREATE INDEX "IX_Events_Timestamp"
ON "Events" ("Timestamp" DESC);

-- Tenant index for multi-tenant queries
CREATE INDEX "IX_Streams_TenantId"
ON "Streams" ("TenantId");

-- Optional: Index on StreamId prefix for pattern-based queries
-- Useful if you use prefixes like "order:", "document-lifecycle:", etc.
CREATE INDEX "IX_Events_StreamId_Prefix"
ON "Events" ("StreamId" varchar_pattern_ops);


-- Step 11: Update statistics
-- ----------------------------------------------------------------

ANALYZE "Streams";
ANALYZE "Events";


-- Step 12: Verification queries
-- ----------------------------------------------------------------

-- Count streams by ID prefix (to see distribution of stream types)
SELECT 
    CASE 
        WHEN "Id" ~ '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' 
            THEN 'GUID (no prefix)'
        ELSE SPLIT_PART("Id", ':', 1)
    END AS stream_type,
    COUNT(*) as count
FROM "Streams"
GROUP BY 1
ORDER BY 2 DESC;

-- Verify event counts match
SELECT 
    'Streams' as table_name, 
    COUNT(*) as row_count,
    SUM("CurrentVersion") as total_events
FROM "Streams"
UNION ALL
SELECT 
    'Events' as table_name,
    COUNT(*) as row_count,
    MAX("Version") as max_version
FROM "Events";

COMMIT;

RAISE NOTICE '=================================================================';
RAISE NOTICE 'Migration completed successfully!';
RAISE NOTICE 'Please verify the application works correctly before celebrating.';
RAISE NOTICE '=================================================================';


-- ============================================================================
-- SQL Server Migration Script
-- ============================================================================

/*
BEGIN TRANSACTION;

-- Step 1: Add new string ID columns
ALTER TABLE [Streams] 
ADD [Id_New] NVARCHAR(255) NULL;

ALTER TABLE [Events] 
ADD [StreamId_New] NVARCHAR(255) NULL;

-- Step 2: Populate new columns
UPDATE [Streams] 
SET [Id_New] = CASE 
    WHEN [StreamType] = '' OR [StreamType] IS NULL 
        THEN CAST([Id] AS NVARCHAR(36))
    ELSE [StreamType] + ':' + CAST([Id] AS NVARCHAR(36))
END;

UPDATE [Events]
SET [StreamId_New] = CASE
    WHEN [StreamType] = '' OR [StreamType] IS NULL
        THEN CAST([StreamId] AS NVARCHAR(36))
    ELSE [StreamType] + ':' + CAST([StreamId] AS NVARCHAR(36))
END;

-- Step 3: Validation
IF EXISTS (SELECT 1 FROM [Streams] WHERE [Id_New] IS NULL)
BEGIN
    RAISERROR('Found NULL values in Streams.Id_New - ABORTING', 16, 1);
    ROLLBACK;
    RETURN;
END;

IF EXISTS (SELECT 1 FROM [Events] WHERE [StreamId_New] IS NULL)
BEGIN
    RAISERROR('Found NULL values in Events.StreamId_New - ABORTING', 16, 1);
    ROLLBACK;
    RETURN;
END;

-- Check for duplicates
IF EXISTS (
    SELECT [TenantId], [Id_New], COUNT(*) as cnt
    FROM [Streams]
    GROUP BY [TenantId], [Id_New]
    HAVING COUNT(*) > 1
)
BEGIN
    RAISERROR('Found duplicate stream IDs - ABORTING', 16, 1);
    ROLLBACK;
    RETURN;
END;

-- Step 4: Drop constraints
ALTER TABLE [Events] 
DROP CONSTRAINT [FK_Events_Streams];

ALTER TABLE [Streams] 
DROP CONSTRAINT [PK_Streams];

ALTER TABLE [Events]
DROP CONSTRAINT [PK_Events];

-- Step 5: Drop old columns
ALTER TABLE [Streams] DROP COLUMN [StreamType];
ALTER TABLE [Events] DROP COLUMN [StreamType];
ALTER TABLE [Streams] DROP COLUMN [Id];
ALTER TABLE [Events] DROP COLUMN [StreamId];

-- Step 6: Rename columns
EXEC sp_rename 'Streams.Id_New', 'Id', 'COLUMN';
EXEC sp_rename 'Events.StreamId_New', 'StreamId', 'COLUMN';

-- Step 7: Make NOT NULL
ALTER TABLE [Streams] ALTER COLUMN [Id] NVARCHAR(255) NOT NULL;
ALTER TABLE [Events] ALTER COLUMN [StreamId] NVARCHAR(255) NOT NULL;

-- Step 8: Recreate primary keys
ALTER TABLE [Streams]
ADD CONSTRAINT [PK_Streams] PRIMARY KEY CLUSTERED ([TenantId], [Id]);

ALTER TABLE [Events]
ADD CONSTRAINT [PK_Events] PRIMARY KEY CLUSTERED ([TenantId], [StreamId], [Version]);

-- Step 9: Recreate foreign key
ALTER TABLE [Events]
ADD CONSTRAINT [FK_Events_Streams]
    FOREIGN KEY ([TenantId], [StreamId])
    REFERENCES [Streams] ([TenantId], [Id])
    ON DELETE CASCADE;

-- Step 10: Recreate indexes
CREATE NONCLUSTERED INDEX [IX_Events_Sequence]
ON [Events] ([Sequence] ASC);

CREATE NONCLUSTERED INDEX [IX_Events_Timestamp]
ON [Events] ([Timestamp] DESC);

-- Step 11: Update statistics
UPDATE STATISTICS [Streams];
UPDATE STATISTICS [Events];

COMMIT;

PRINT '=================================================================';
PRINT 'Migration completed successfully!';
PRINT '=================================================================';
*/


-- ============================================================================
-- Rollback Script (PostgreSQL)
-- ============================================================================
-- IMPORTANT: This only works if you haven't dropped the backup tables yet!
-- ============================================================================

/*
BEGIN TRANSACTION;

-- Only use this if you created backup tables before migration
-- CREATE TABLE "Streams_Backup" AS SELECT * FROM "Streams";
-- CREATE TABLE "Events_Backup" AS SELECT * FROM "Events";

-- Restore from backup
TRUNCATE TABLE "Events" CASCADE;
TRUNCATE TABLE "Streams" CASCADE;

INSERT INTO "Streams" SELECT * FROM "Streams_Backup";
INSERT INTO "Events" SELECT * FROM "Events_Backup";

-- Recreate original constraints and indexes
-- (Add your original constraint creation SQL here)

COMMIT;

RAISE NOTICE 'Rollback completed. Original data restored.';
*/


-- ============================================================================
-- Performance Comparison Queries
-- ============================================================================
-- Run these before and after migration to compare performance
-- ============================================================================

-- Query 1: Fetch a specific stream
EXPLAIN ANALYZE
SELECT * FROM "Streams" 
WHERE "TenantId" = '00000000-0000-0000-0000-000000000000'
  AND "Id" = '550e8400-e29b-41d4-a716-446655440000';

-- Query 2: Fetch events for a stream
EXPLAIN ANALYZE
SELECT * FROM "Events"
WHERE "TenantId" = '00000000-0000-0000-0000-000000000000'
  AND "StreamId" = '550e8400-e29b-41d4-a716-446655440000'
ORDER BY "Version" ASC;

-- Query 3: Fetch streams by prefix (new capability in v2.0)
EXPLAIN ANALYZE
SELECT * FROM "Streams"
WHERE "TenantId" = '00000000-0000-0000-0000-000000000000'
  AND "Id" LIKE 'order:%';

-- Query 4: Global event sequence query
EXPLAIN ANALYZE
SELECT * FROM "Events"
WHERE "Sequence" > 1000000
ORDER BY "Sequence" ASC
LIMIT 100;


-- ============================================================================
-- Data Quality Checks (Post-Migration)
-- ============================================================================

-- Check 1: Verify all events have corresponding streams
SELECT COUNT(*) as orphaned_events
FROM "Events" e
WHERE NOT EXISTS (
    SELECT 1 FROM "Streams" s
    WHERE s."TenantId" = e."TenantId"
      AND s."Id" = e."StreamId"
);
-- Expected: 0

-- Check 2: Verify stream version matches event count
SELECT 
    s."Id",
    s."CurrentVersion" as stream_version,
    COUNT(e."Version") as event_count,
    s."CurrentVersion" - COUNT(e."Version") as difference
FROM "Streams" s
LEFT JOIN "Events" e ON s."TenantId" = e."TenantId" AND s."Id" = e."StreamId"
GROUP BY s."Id", s."CurrentVersion"
HAVING s."CurrentVersion" != COUNT(e."Version");
-- Expected: 0 rows (no mismatches)

-- Check 3: Verify no duplicate events
SELECT "TenantId", "StreamId", "Version", COUNT(*) as duplicate_count
FROM "Events"
GROUP BY "TenantId", "StreamId", "Version"
HAVING COUNT(*) > 1;
-- Expected: 0 rows

-- Check 4: Verify stream ID format distribution
SELECT 
    CASE 
        WHEN "Id" ~ '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' 
            THEN 'Pure GUID'
        WHEN "Id" LIKE '%:%'
            THEN 'Prefixed (type:guid)'
        ELSE 'Other format'
    END AS id_format,
    COUNT(*) as stream_count,
    AVG(LENGTH("Id")) as avg_length,
    MIN(LENGTH("Id")) as min_length,
    MAX(LENGTH("Id")) as max_length
FROM "Streams"
GROUP BY 1
ORDER BY 2 DESC;

-- Check 5: Find longest stream IDs (potential performance concerns)
SELECT "Id", LENGTH("Id") as length
FROM "Streams"
ORDER BY LENGTH("Id") DESC
LIMIT 20;
-- Expected: Most IDs should be < 100 characters


-- ============================================================================
-- Index Size Comparison
-- ============================================================================

-- PostgreSQL: Check index sizes
SELECT
    schemaname,
    tablename,
    indexname,
    pg_size_pretty(pg_relation_size(indexname::regclass)) AS index_size
FROM pg_indexes
WHERE tablename IN ('Streams', 'Events')
ORDER BY pg_relation_size(indexname::regclass) DESC;

-- PostgreSQL: Check table sizes
SELECT
    schemaname,
    tablename,
    pg_size_pretty(pg_total_relation_size(schemaname||'.'||tablename)) AS total_size,
    pg_size_pretty(pg_relation_size(schemaname||'.'||tablename)) AS table_size,
    pg_size_pretty(pg_total_relation_size(schemaname||'.'||tablename) - pg_relation_size(schemaname||'.'||tablename)) AS index_size
FROM pg_tables
WHERE tablename IN ('Streams', 'Events')
ORDER BY pg_total_relation_size(schemaname||'.'||tablename) DESC;
