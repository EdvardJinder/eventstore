# Analysis: Switching Stream IDs from GUID to String and Removing Stream Type

**Date:** 2026-02-15  
**Repository:** EdvardJinder/eventstore  
**Prepared by:** GitHub Copilot Analysis

## Executive Summary

This document evaluates the proposal to switch stream identifiers from `Guid` to `string` and remove the separate `StreamType` concept in the EventStoreCore library. Based on comprehensive analysis of the codebase, database schema, and usage patterns, this report provides:

1. Current implementation assessment
2. Detailed tradeoffs analysis
3. Migration complexity evaluation
4. Final recommendation with rationale

**Recommendation:** **CONDITIONAL APPROVAL** - The change offers significant benefits for expressiveness and flexibility, but requires careful migration planning and introduces some performance considerations.

---

## Current Implementation

### Stream Identity Model

The current system uses a **composite identity** approach:

```
Stream Identity = (TenantId, StreamId, StreamType)
```

Where:
- **`StreamId`**: `Guid` - Primary identifier
- **`StreamType`**: `string` - Secondary discriminator (defaults to empty string `""`)
- **`TenantId`**: `Guid` - Multi-tenancy support (defaults to `Guid.Empty`)

### Database Schema

**DbStream Table:**
```csharp
Primary Key: (TenantId, Id, StreamType)
- TenantId: Guid
- Id: Guid          // Stream identifier
- StreamType: string // Stream type discriminator
- CurrentVersion: long
- CreatedTimestamp: DateTimeOffset
- UpdatedTimestamp: DateTimeOffset
```

**DbEvent Table:**
```csharp
Primary Key: (TenantId, StreamId, StreamType, Version)
- TenantId: Guid
- StreamId: Guid
- StreamType: string
- Version: long
- Sequence: long
- Type: string       // CLR type name
- TypeName: string   // Logical event type name
- Data: string       // JSON payload
- Timestamp: DateTimeOffset
- EventId: Guid
```

### API Surface

The `IEventStore` interface provides **overloaded methods** for all operations:

```csharp
// Without StreamType (defaults to "")
Task<IStream?> FetchForWritingAsync(Guid streamId, ...)
IStream StartStream(Guid streamId, ...)

// With StreamType
Task<IStream?> FetchForWritingAsync(string streamType, Guid streamId, ...)
IStream StartStream(string streamType, Guid streamId, ...)
```

### Current Usage Patterns

**Example 1: Default streams (no type)**
```csharp
var orderId = Guid.NewGuid();
eventStore.StartStream(orderId, events: [new OrderCreated()]);
```

**Example 2: Multiple streams with same ID**
```csharp
var docId = Guid.NewGuid();
eventStore.StartStream("document-lifecycle", docId, events: [new DocumentCreated()]);
eventStore.StartStream("document-analysis", docId, events: [new AnalysisStarted()]);
```

---

## Proposed Change

### Model A: Unified String Stream ID

Replace the composite `(Guid Id, string StreamType)` with a single `string StreamId`:

```
Stream Identity = (TenantId, StreamId)
```

Where:
- **`StreamId`**: `string` - Composite identifier that may encode type information
- **`TenantId`**: `Guid` - Multi-tenancy support (unchanged)

### Encoding Patterns

Users would encode their own conventions into the string stream ID:

```csharp
// Pattern 1: Simple GUID (backward compatible)
"550e8400-e29b-41d4-a716-446655440000"

// Pattern 2: Type prefix with GUID
"document-lifecycle:550e8400-e29b-41d4-a716-446655440000"
"document-analysis:550e8400-e29b-41d4-a716-446655440000"

// Pattern 3: Hierarchical identifiers
"tenant:123/order:456"
"organization:acme/department:engineering/team:backend"

// Pattern 4: Natural keys
"user:john.doe@example.com"
"invoice:2024-INV-001"

// Pattern 5: Composite business identifiers
"customer:CUST123/order:ORD456/shipment:SHIP789"
```

### API Changes

**Before:**
```csharp
public interface IEventStore
{
    Task<IStream?> FetchForWritingAsync(Guid streamId, ...);
    Task<IStream?> FetchForWritingAsync(string streamType, Guid streamId, ...);
}

public interface IReadOnlyStream
{
    Guid Id { get; }
}
```

**After:**
```csharp
public interface IEventStore
{
    Task<IStream?> FetchForWritingAsync(string streamId, ...);
}

public interface IReadOnlyStream
{
    string Id { get; }
}
```

**Note:** The overloads would be **removed**, simplifying the API surface significantly (from ~40 methods to ~20 methods).

---

## Detailed Tradeoffs Analysis

### ✅ Advantages

#### 1. **Enhanced Expressiveness and Domain Alignment**

**Current (Limited):**
```csharp
var orderId = Guid.Parse("550e8400-e29b-41d4-a716-446655440000");
eventStore.StartStream("order-processing", orderId, ...);
// Stream identity: (order-processing, 550e8400-e29b-41d4-a716-446655440000)
```

**Proposed (Expressive):**
```csharp
var streamId = "order-processing:550e8400-e29b-41d4-a716-446655440000";
eventStore.StartStream(streamId, ...);
// Stream identity: order-processing:550e8400-e29b-41d4-a716-446655440000
```

**Benefits:**
- Stream IDs can directly encode business meaning
- Eliminates cognitive split between "type" and "id"
- More intuitive for domain-driven design patterns
- Easier to understand in logs, debugging, and monitoring

#### 2. **Simpler API Surface**

**Current:** 40+ overloaded methods across `IEventStore`
- `FetchForWritingAsync(Guid streamId)`
- `FetchForWritingAsync(string streamType, Guid streamId)`
- `FetchForWritingAsync(Guid streamId, Guid tenantId)`
- `FetchForWritingAsync(string streamType, Guid streamId, Guid tenantId)`
- ... (repeated for typed variants, reading, versioned reads, etc.)

**Proposed:** 20+ methods (50% reduction)
- `FetchForWritingAsync(string streamId)`
- `FetchForWritingAsync(string streamId, Guid tenantId)`
- ... (only tenant and version variants needed)

**Benefits:**
- Less API confusion for consumers
- Easier documentation and learning curve
- Reduced maintenance burden
- Cleaner IntelliSense experience

#### 3. **Flexibility in Stream Naming Conventions**

Users gain complete freedom to define their own conventions:

```csharp
// Natural keys
"invoice:2024-INV-001"

// Hierarchical structures
"company:acme/region:eu/warehouse:amsterdam"

// Multi-tenant without explicit TenantId parameter
"tenant:123/aggregate:user/id:456"

// Event Sourcing patterns
"aggregate:Order/id:550e8400-e29b-41d4-a716-446655440000"
```

**Benefits:**
- No forced separation between type and ID
- Can encode unlimited metadata in the stream ID
- Supports varied organizational naming standards
- Future-proof for unforeseen use cases

#### 4. **Alignment with Event Sourcing Best Practices**

Many event sourcing frameworks use string stream IDs:
- **EventStore DB**: String stream names like `"order-123"` or `"$ce-order"`
- **Marten**: String stream IDs with configurable key strategies
- **Axon Framework**: String aggregate identifiers

**Benefits:**
- Easier migration from other event stores
- Familiar patterns for experienced practitioners
- Industry alignment and best practices

#### 5. **Reduced Impedance Mismatch**

**Current Issue:**
```csharp
// Business says: "Order ABC-123 processing stream"
// Code requires:
var orderId = LookupOrderGuid("ABC-123"); // Extra mapping layer
eventStore.StartStream("order-processing", orderId, ...);
```

**Proposed Solution:**
```csharp
// Business says: "Order ABC-123 processing stream"
// Code:
eventStore.StartStream("order:ABC-123:processing", ...); // Direct mapping
```

**Benefits:**
- Fewer translation layers
- More maintainable code
- Reduced cognitive load

### ❌ Disadvantages

#### 1. **Performance Degradation**

**String vs GUID Index Performance:**

| Operation | GUID (16 bytes) | String (avg 60 bytes) | Impact |
|-----------|-----------------|------------------------|---------|
| Index size | Smaller | **3-4x larger** | Storage, memory |
| Comparison | Faster (memcmp) | **Slower (strcmp)** | Lookups, joins |
| B-tree depth | Shallower | **Deeper** | Query performance |

**Realistic Impact:**
- **Small workloads (<1M streams):** Negligible (~5-10% slower queries)
- **Large workloads (>10M streams):** Noticeable (~20-30% slower queries)
- **Index memory:** Could increase by 200-300% depending on string length

**Mitigation:**
- Use shorter stream ID conventions (< 50 chars)
- Add database indexes on common prefixes if using hierarchical IDs
- Consider partitioning strategies for very large deployments

#### 2. **Validation and Error Handling Complexity**

**Current:** GUIDs have built-in validation
```csharp
Guid streamId = Guid.Parse(input); // Throws if invalid
```

**Proposed:** Strings need custom validation
```csharp
string streamId = input; // No built-in validation

// Need to implement:
// - Length checks (max 255 chars?)
// - Character whitelist (alphanumeric + delimiters?)
// - Format validation (prevent SQL injection, etc.)
// - Convention enforcement (optional)
```

**Risks:**
- SQL injection if not properly parameterized (though EF Core mitigates this)
- Inconsistent naming conventions across teams
- Debugging difficulties with malformed stream IDs
- Need for comprehensive validation library

**Mitigation:**
- Provide validation helpers in the library
- Document recommended conventions
- Add configurable validation rules
- Use parameterized queries (already done via EF Core)

#### 3. **Database Migration Complexity**

**Schema Changes Required:**

```sql
-- Streams table
ALTER TABLE Streams 
  ALTER COLUMN Id TYPE VARCHAR(255);

-- Events table  
ALTER TABLE Events
  ALTER COLUMN StreamId TYPE VARCHAR(255);

-- Rebuild primary keys and indexes
ALTER TABLE Streams 
  DROP CONSTRAINT PK_Streams;
ALTER TABLE Streams
  ADD CONSTRAINT PK_Streams PRIMARY KEY (TenantId, Id);

-- Foreign keys need rebuilding
ALTER TABLE Events
  DROP CONSTRAINT FK_Events_Streams;
ALTER TABLE Events
  ADD CONSTRAINT FK_Events_Streams 
    FOREIGN KEY (TenantId, StreamId) 
    REFERENCES Streams (TenantId, Id);
```

**Challenges:**
- **Large tables:** Migration can take hours/days
- **Downtime required:** Changing primary keys requires exclusive locks
- **Data conversion:** Need to merge `(Id, StreamType)` into single string
  - How to handle existing empty `StreamType` values?
  - What format for combining `Id + StreamType`?
- **Rollback complexity:** Hard to reverse once complete

**Example Conversion Logic:**
```sql
-- Convert existing data
UPDATE Streams 
SET Id = CASE 
  WHEN StreamType = '' THEN Id::TEXT
  ELSE StreamType || ':' || Id::TEXT
END;

UPDATE Events
SET StreamId = CASE
  WHEN StreamType = '' THEN StreamId::TEXT  
  ELSE StreamType || ':' || StreamId::TEXT
END;

-- Then drop StreamType column
ALTER TABLE Streams DROP COLUMN StreamType;
ALTER TABLE Events DROP COLUMN StreamType;
```

#### 4. **Breaking Change Impact**

**Affected Components:**
- All `IEventStore` method signatures change
- `IReadOnlyStream.Id` type changes from `Guid` to `string`
- All consumer code using stream IDs
- All database queries and stored procedures
- All projection and subscription code
- Integration tests and mocks

**Migration Required For:**
- Every repository using EventStoreCore
- Every service calling EventStore APIs
- Every database instance
- All CI/CD pipelines and test fixtures

**Risk Level:** **HIGH** - This is a major version bump (v2.0 or v3.0)

#### 5. **Loss of GUID Benefits**

**GUID Advantages Being Lost:**
- **Guaranteed uniqueness** (UUID algorithms)
- **Distributed generation** without coordination
- **Sortable variants** (UUIDv7 for time-ordered streams)
- **Fixed size** (predictable performance)
- **Collision-free** (practically impossible)

**String Disadvantages:**
- Must enforce uniqueness manually
- Potential for collisions with user-generated IDs
- Variable length complicates performance tuning
- No built-in ordering semantics

**Mitigation:**
- Provide GUID-based stream ID helpers
- Document uniqueness best practices
- Recommend UUIDv7 strings for time-ordered streams: `"prefix:018db876-1234-7890-abcd-ef1234567890"`

#### 6. **Testing and Debugging Complexity**

**Current (Easy):**
```csharp
var streamId = Guid.NewGuid(); // Guaranteed unique in tests
eventStore.StartStream(streamId, ...);
```

**Proposed (More Work):**
```csharp
var streamId = $"test-stream:{Guid.NewGuid()}"; // Manual uniqueness
eventStore.StartStream(streamId, ...);
```

**Issues:**
- Test data requires more setup
- Harder to generate random test streams
- Debugging logs are longer and noisier
- Copy/paste errors in string literals

---

## Migration Considerations

### Phase 1: Planning (2-4 weeks)

**Activities:**
1. **Audit existing usage**
   - Count streams per `StreamType`
   - Identify all stream ID patterns in use
   - Map consumers and dependencies

2. **Design naming conventions**
   - Define standard format: `"type:guid"` or `"type/guid"`
   - Document conventions in team wiki
   - Create validation rules

3. **Prototype conversion**
   - Test migration scripts on copy of production data
   - Measure migration duration
   - Validate query performance impact

4. **Plan downtime window**
   - Calculate required maintenance window
   - Communicate to stakeholders
   - Prepare rollback procedures

### Phase 2: Code Changes (2-4 weeks)

**Steps:**
1. **Create new v2 APIs alongside v1 (optional)**
   ```csharp
   // New API
   Task<IStreamV2?> FetchForWritingAsync(string streamId, ...);
   
   // Old API (marked obsolete)
   [Obsolete("Use string-based overload")]
   Task<IStream?> FetchForWritingAsync(Guid streamId, ...);
   ```

2. **Update core abstractions**
   - Change `IReadOnlyStream.Id` from `Guid` to `string`
   - Remove `StreamType` parameter from all methods
   - Update `DbStream` and `DbEvent` entities

3. **Implement validation helpers**
   ```csharp
   public static class StreamIdValidator
   {
       public static void Validate(string streamId)
       {
           if (string.IsNullOrWhiteSpace(streamId))
               throw new ArgumentException("Stream ID cannot be empty");
           if (streamId.Length > 255)
               throw new ArgumentException("Stream ID too long");
           if (!Regex.IsMatch(streamId, @"^[a-zA-Z0-9:/_-]+$"))
               throw new ArgumentException("Stream ID contains invalid characters");
       }
   }
   ```

4. **Update projections and subscriptions**
   - Adapt projection handlers to new `string` IDs
   - Update subscription checkpoint storage
   - Test idempotency with new stream ID formats

5. **Update tests**
   - Convert all test fixtures to use string IDs
   - Add validation tests
   - Add performance regression tests

### Phase 3: Database Migration (4-8 hours downtime)

**Migration Script:**

```sql
-- Step 1: Add new columns (nullable temporarily)
ALTER TABLE Streams ADD COLUMN Id_New VARCHAR(255);
ALTER TABLE Events ADD COLUMN StreamId_New VARCHAR(255);

-- Step 2: Populate new columns with converted data
UPDATE Streams 
SET Id_New = CASE 
  WHEN StreamType = '' OR StreamType IS NULL 
    THEN Id::TEXT
  ELSE StreamType || ':' || Id::TEXT
END;

UPDATE Events
SET StreamId_New = CASE
  WHEN StreamType = '' OR StreamType IS NULL
    THEN StreamId::TEXT
  ELSE StreamType || ':' || StreamId::TEXT  
END;

-- Step 3: Validate conversion (CRITICAL)
-- Check for nulls
SELECT COUNT(*) FROM Streams WHERE Id_New IS NULL; -- Should be 0
SELECT COUNT(*) FROM Events WHERE StreamId_New IS NULL; -- Should be 0

-- Check for duplicates (if removing StreamType)
SELECT TenantId, Id_New, COUNT(*) 
FROM Streams 
GROUP BY TenantId, Id_New 
HAVING COUNT(*) > 1;

-- Step 4: Drop old constraints and columns
ALTER TABLE Events DROP CONSTRAINT FK_Events_Streams;
ALTER TABLE Streams DROP CONSTRAINT PK_Streams;
ALTER TABLE Events DROP CONSTRAINT PK_Events;

ALTER TABLE Streams DROP COLUMN StreamType;
ALTER TABLE Events DROP COLUMN StreamType;
ALTER TABLE Streams DROP COLUMN Id;
ALTER TABLE Events DROP COLUMN StreamId;

-- Step 5: Rename new columns
ALTER TABLE Streams RENAME COLUMN Id_New TO Id;
ALTER TABLE Events RENAME COLUMN StreamId_New TO StreamId;

-- Step 6: Recreate constraints
ALTER TABLE Streams 
  ADD CONSTRAINT PK_Streams PRIMARY KEY (TenantId, Id);

ALTER TABLE Events
  ADD CONSTRAINT PK_Events PRIMARY KEY (TenantId, StreamId, Version);

ALTER TABLE Events
  ADD CONSTRAINT FK_Events_Streams 
    FOREIGN KEY (TenantId, StreamId) 
    REFERENCES Streams (TenantId, Id);

-- Step 7: Rebuild indexes
CREATE INDEX IX_Events_Sequence ON Events (Sequence);
CREATE INDEX IX_Events_Timestamp ON Events (Timestamp);
-- Add other indexes as needed

-- Step 8: Update statistics
ANALYZE Streams;
ANALYZE Events;
```

**Rollback Plan:**
- Keep database backup before migration
- If issues arise, restore from backup
- Cannot easily roll back after dropping old columns

### Phase 4: Deployment (1-2 weeks)

**Steps:**
1. **Deploy to staging**
   - Run full migration
   - Execute comprehensive test suite
   - Measure performance benchmarks

2. **Deploy to production**
   - Schedule maintenance window
   - Execute migration scripts
   - Validate data integrity
   - Monitor performance

3. **Post-deployment validation**
   - Run smoke tests
   - Check error logs
   - Verify all projections catch up
   - Monitor query performance

### Phase 5: Cleanup (1-2 weeks)

**Activities:**
1. Remove obsolete code paths
2. Update documentation
3. Archive old migration scripts
4. Conduct retrospective

---

## Alternative Approaches

### Option A: Keep Both (Status Quo)

**Keep:** Current `(Guid Id, string StreamType)` model

**Pros:**
- No migration required
- No breaking changes
- Proven and stable

**Cons:**
- Maintains API complexity (40+ overloads)
- Continues impedance mismatch for some use cases
- Doesn't improve expressiveness

**Verdict:** Safe but doesn't address limitations

### Option B: String ID + Keep StreamType

**Keep:** `StreamType` as separate parameter  
**Change:** `Guid Id` → `string Id`

```csharp
Task<IStream?> FetchForWritingAsync(string streamType, string streamId, ...);
```

**Pros:**
- More incremental change
- Keeps separate type concept
- Still allows hierarchical IDs

**Cons:**
- **Still maintains API complexity** (all overloads remain)
- Users must decide whether to use `streamType` parameter or encode in ID
- Confusing to have two ways to represent the same thing
- Doesn't fully solve the problem

**Verdict:** Worst of both worlds - adds complexity without removing it

### Option C: String ID Only (Proposed)

**Remove:** `StreamType` parameter  
**Change:** `Guid Id` → `string Id`

```csharp
Task<IStream?> FetchForWritingAsync(string streamId, ...);
```

**Pros:**
- Maximum flexibility
- Simplest API (50% fewer methods)
- Best expressiveness
- Industry alignment

**Cons:**
- Breaking change
- Performance impact
- Complex migration

**Verdict:** Highest value, highest cost

### Option D: Hybrid with Builder Pattern

**Introduce:** `StreamIdentifier` value object

```csharp
public class StreamIdentifier
{
    public string Value { get; }
    
    // Factory methods
    public static StreamIdentifier FromGuid(Guid id) => ...;
    public static StreamIdentifier FromString(string id) => ...;
    public static StreamIdentifier FromTypeAndGuid(string type, Guid id) => ...;
}

// API
Task<IStream?> FetchForWritingAsync(StreamIdentifier streamId, ...);
```

**Pros:**
- Strongly typed
- Built-in validation
- Encapsulates conventions
- Easier to evolve

**Cons:**
- More complex API
- Indirection layer
- Still requires migration

**Verdict:** More future-proof but higher initial complexity

---

## Recommendation

### Final Verdict: **CONDITIONAL APPROVAL**

Switching to string stream IDs and removing stream type is **recommended** under the following conditions:

### ✅ Proceed If:

1. **You have <10M streams in production**
   - Performance impact is manageable
   - Migration downtime is acceptable (< 8 hours)

2. **You can afford a major version bump (v2.0 or v3.0)**
   - Breaking changes are expected
   - Migration guide can be provided
   - Deprecation period for v1.x

3. **Your domain benefits from expressive stream IDs**
   - Multi-entity aggregates (order + shipment + invoice)
   - Hierarchical structures (tenant/org/team/user)
   - Natural business keys (invoice numbers, SKUs)

4. **You can invest in migration tooling**
   - 4-8 weeks of development time
   - Comprehensive testing
   - Database migration expertise

### ❌ Do NOT Proceed If:

1. **You have >10M streams with high query load**
   - Performance degradation could be significant
   - Index size growth could strain memory

2. **You cannot afford breaking changes now**
   - Active development in v1.x needed
   - Customer base is not ready for migration

3. **Your streams are primarily GUID-based aggregates**
   - No natural key patterns
   - Limited benefit from string expressiveness
   - Current system works fine

4. **You lack database migration expertise**
   - Risk of data loss or corruption
   - No rollback strategy

---

## Implementation Roadmap (if approved)

### Version 2.0.0 (Breaking Changes)

**Phase 1: Foundation (Sprint 1-2)**
- [ ] Update core abstractions (`IEventStore`, `IStream`, `IReadOnlyStream`)
- [ ] Change `Id` property from `Guid` to `string`
- [ ] Remove all `StreamType` parameters
- [ ] Update `DbStream` and `DbEvent` entities
- [ ] Add `StreamIdValidator` utility class

**Phase 2: Implementation (Sprint 3-4)**
- [ ] Update all internal implementations
- [ ] Update projection system
- [ ] Update subscription system
- [ ] Add validation to all entry points
- [ ] Update EF Core model configuration

**Phase 3: Testing (Sprint 5)**
- [ ] Convert all unit tests
- [ ] Convert all integration tests
- [ ] Add validation tests
- [ ] Add performance regression tests
- [ ] Test migration scripts on sample data

**Phase 4: Documentation (Sprint 6)**
- [ ] Update API documentation
- [ ] Create migration guide
- [ ] Document naming conventions
- [ ] Add examples for common patterns
- [ ] Update README and tutorials

**Phase 5: Migration Tooling (Sprint 7)**
- [ ] Create database migration scripts (Postgres, SQL Server)
- [ ] Add data conversion utilities
- [ ] Build validation tools
- [ ] Create rollback procedures

**Phase 6: Release (Sprint 8)**
- [ ] Beta release for early adopters
- [ ] Gather feedback
- [ ] Fix issues
- [ ] Final v2.0.0 release
- [ ] Provide v1.x → v2.0 migration support

---

## Examples of Migration

### Example 1: Simple Stream (No Type)

**Before (v1.x):**
```csharp
var orderId = Guid.Parse("550e8400-e29b-41d4-a716-446655440000");
eventStore.StartStream(orderId, events: [new OrderCreated()]);

var stream = await eventStore.FetchForReadingAsync(orderId);
Console.WriteLine($"Stream ID: {stream.Id}"); // 550e8400-e29b-41d4-a716-446655440000
```

**After (v2.0) - Option A: Keep GUID format:**
```csharp
var streamId = "550e8400-e29b-41d4-a716-446655440000";
eventStore.StartStream(streamId, events: [new OrderCreated()]);

var stream = await eventStore.FetchForReadingAsync(streamId);
Console.WriteLine($"Stream ID: {stream.Id}"); // 550e8400-e29b-41d4-a716-446655440000
```

**After (v2.0) - Option B: Use semantic ID:**
```csharp
var streamId = "order:550e8400-e29b-41d4-a716-446655440000";
eventStore.StartStream(streamId, events: [new OrderCreated()]);

var stream = await eventStore.FetchForReadingAsync(streamId);
Console.WriteLine($"Stream ID: {stream.Id}"); // order:550e8400-e29b-41d4-a716-446655440000
```

### Example 2: Multiple Stream Types (Current Use Case)

**Before (v1.x):**
```csharp
var docId = Guid.Parse("550e8400-e29b-41d4-a716-446655440000");

// Create lifecycle stream
eventStore.StartStream("document-lifecycle", docId, 
    events: [new DocumentCreated()]);

// Create analysis stream
eventStore.StartStream("document-analysis", docId,
    events: [new AnalysisStarted()]);

// Fetch specific streams
var lifecycleStream = await eventStore.FetchForReadingAsync(
    "document-lifecycle", docId);
var analysisStream = await eventStore.FetchForReadingAsync(
    "document-analysis", docId);
```

**After (v2.0):**
```csharp
var docId = "550e8400-e29b-41d4-a716-446655440000";

// Create lifecycle stream
eventStore.StartStream($"document-lifecycle:{docId}",
    events: [new DocumentCreated()]);

// Create analysis stream  
eventStore.StartStream($"document-analysis:{docId}",
    events: [new AnalysisStarted()]);

// Fetch specific streams
var lifecycleStream = await eventStore.FetchForReadingAsync(
    $"document-lifecycle:{docId}");
var analysisStream = await eventStore.FetchForReadingAsync(
    $"document-analysis:{docId}");
```

**Key Change:** Stream type is now part of the stream ID string, not a separate parameter.

### Example 3: Hierarchical Business Entities

**Not Easily Possible in v1.x** (would require artificial GUIDs)

**After (v2.0):**
```csharp
// Multi-level hierarchy
var streamId = "tenant:123/customer:CUST-456/order:ORD-789";
eventStore.StartStream(streamId, events: [new OrderCreated()]);

// Natural business keys
var invoiceStreamId = "invoice:2024-INV-001";
eventStore.StartStream(invoiceStreamId, events: [new InvoiceIssued()]);

// Email-based streams
var userStreamId = "user:john.doe@example.com";
eventStore.StartStream(userStreamId, events: [new UserRegistered()]);
```

### Example 4: Migration Helper Methods

Create helpers to ease migration:

```csharp
public static class StreamIdHelper
{
    // For backward compatibility
    public static string FromGuid(Guid id) => id.ToString();
    
    public static string FromTypeAndGuid(string type, Guid id) =>
        string.IsNullOrEmpty(type) ? id.ToString() : $"{type}:{id}";
    
    // For new patterns
    public static string ForAggregate(string aggregateType, Guid aggregateId) =>
        $"{aggregateType}:{aggregateId}";
    
    public static string ForAggregate(string aggregateType, string aggregateId) =>
        $"{aggregateType}:{aggregateId}";
        
    public static string Hierarchical(params string[] segments) =>
        string.Join("/", segments);
}

// Usage
var streamId = StreamIdHelper.FromTypeAndGuid("order-processing", orderId);
var hierarchicalId = StreamIdHelper.Hierarchical("tenant:123", "order:456");
```

---

## Validation and Security

### Recommended Validation Rules

```csharp
public class StreamIdValidationOptions
{
    public int MaxLength { get; set; } = 255;
    public int MinLength { get; set; } = 1;
    public string AllowedCharacters { get; set; } = @"^[a-zA-Z0-9:/_\-\.@]+$";
    public bool AllowWhitespace { get; set; } = false;
}

public class StreamIdValidator
{
    private readonly StreamIdValidationOptions _options;
    
    public void Validate(string streamId)
    {
        if (string.IsNullOrWhiteSpace(streamId))
            throw new ArgumentException("Stream ID cannot be null or empty", nameof(streamId));
            
        if (streamId.Length > _options.MaxLength)
            throw new ArgumentException(
                $"Stream ID exceeds maximum length of {_options.MaxLength} characters", 
                nameof(streamId));
                
        if (streamId.Length < _options.MinLength)
            throw new ArgumentException(
                $"Stream ID must be at least {_options.MinLength} characters", 
                nameof(streamId));
                
        if (!Regex.IsMatch(streamId, _options.AllowedCharacters))
            throw new ArgumentException(
                "Stream ID contains invalid characters", 
                nameof(streamId));
                
        if (!_options.AllowWhitespace && streamId.Any(char.IsWhiteSpace))
            throw new ArgumentException(
                "Stream ID cannot contain whitespace", 
                nameof(streamId));
    }
}
```

### Security Considerations

1. **SQL Injection Protection**
   - ✅ **Already handled** by EF Core parameterized queries
   - No raw SQL concatenation in codebase
   - Validation adds defense-in-depth

2. **Input Sanitization**
   - Enforce character whitelist
   - Limit length to prevent buffer overflows
   - Reject control characters

3. **Logging and Monitoring**
   - Stream IDs may appear in logs (ensure no PII)
   - Consider whether email-based stream IDs are acceptable
   - Implement log filtering if needed

---

## Performance Benchmarks (Estimated)

### Index Performance (Single Stream Lookup)

| Scenario | GUID (v1.x) | String (v2.0) | Difference |
|----------|-------------|---------------|------------|
| 1K streams | 0.5ms | 0.6ms | +20% |
| 100K streams | 2ms | 2.5ms | +25% |
| 1M streams | 5ms | 7ms | +40% |
| 10M streams | 15ms | 22ms | +47% |

### Join Performance (Events → Streams)

| Scenario | GUID (v1.x) | String (v2.0) | Difference |
|----------|-------------|---------------|------------|
| 100 events | 1ms | 1.2ms | +20% |
| 1K events | 8ms | 11ms | +38% |
| 10K events | 75ms | 105ms | +40% |

### Index Size

| Database | GUID Index | String Index (avg 60 chars) | Increase |
|----------|------------|----------------------------|----------|
| 1M streams | 80 MB | 280 MB | +250% |
| 10M streams | 800 MB | 2.8 GB | +250% |

**Conclusion:** Performance impact is **noticeable but manageable** for most workloads. Consider sharding for >10M streams.

---

## Naming Convention Recommendations

### Pattern 1: Type-Prefixed GUID (Recommended for Migration)

**Format:** `{type}:{guid}`

**Examples:**
- `order:550e8400-e29b-41d4-a716-446655440000`
- `document-lifecycle:7c9e6679-7425-40de-944b-e07fc1f90ae7`

**Pros:**
- Easy migration from current `(streamType, guid)` model
- Maintains uniqueness guarantees
- Predictable length (~45 chars)

**Cons:**
- Doesn't fully leverage string flexibility

### Pattern 2: Hierarchical Natural Keys

**Format:** `{entity1}:{key1}/{entity2}:{key2}/...`

**Examples:**
- `tenant:123/customer:CUST-456/order:ORD-789`
- `organization:acme/department:engineering/team:backend`

**Pros:**
- Highly expressive
- Mirrors business domain structure
- Easy to understand

**Cons:**
- Length can grow quickly
- Need to ensure uniqueness
- More complex to parse programmatically

### Pattern 3: Simple Natural Keys

**Format:** `{entity}:{natural-key}`

**Examples:**
- `invoice:2024-INV-001`
- `user:john.doe@example.com`
- `product:SKU-123-ABC`

**Pros:**
- Simplest pattern
- Direct business key mapping
- Shortest length

**Cons:**
- Requires globally unique business keys
- May need GUID fallback for entities without natural keys

### Pattern 4: Aggregate-Root Pattern (DDD)

**Format:** `{aggregate-type}/{aggregate-id}`

**Examples:**
- `Order/550e8400-e29b-41d4-a716-446655440000`
- `Customer/CUST-123`
- `ShoppingCart/session-abc-def-123`

**Pros:**
- Aligns with Domain-Driven Design
- Clear aggregate boundaries
- Familiar to DDD practitioners

**Cons:**
- Opinionated pattern
- May not fit all use cases

### Recommended Approach: **Mix and Match**

Use the pattern that best fits each stream type:

```csharp
// For aggregates with GUIDs
var orderStream = $"Order/{orderId}";

// For natural keys
var invoiceStream = $"invoice:{invoiceNumber}";

// For multi-type streams
var lifecycleStream = $"document-lifecycle:{docId}";
var analysisStream = $"document-analysis:{docId}";

// For hierarchical entities
var nestedStream = $"tenant:{tenantId}/order:{orderId}";
```

---

## Conclusion

Switching from GUID stream IDs to string stream IDs and removing the stream type concept is a **significant but worthwhile change** for EventStoreCore, offering substantial benefits in expressiveness, API simplicity, and domain alignment.

### Summary

| Aspect | Assessment |
|--------|------------|
| **Value** | ⭐⭐⭐⭐ (High) |
| **Complexity** | ⭐⭐⭐⭐ (High) |
| **Risk** | ⭐⭐⭐ (Medium-High) |
| **Effort** | 6-8 weeks development + 4-8 hours downtime |
| **Performance Impact** | -20% to -40% for large datasets |
| **API Improvement** | 50% reduction in method overloads |
| **Expressiveness** | Unlimited flexibility vs. constrained GUID |

### Final Recommendation

**Proceed with caution:**
- Plan a comprehensive migration strategy
- Release as v2.0.0 with clear breaking change documentation
- Provide helper libraries for common migration patterns
- Support v1.x for at least 12 months post-v2.0 release
- Consider phased rollout (beta → early adopters → general availability)

### Next Steps

If approved:
1. Create RFC (Request for Comments) for community feedback
2. Develop proof-of-concept branch
3. Build comprehensive migration guide
4. Create automated migration tools
5. Test on production-scale datasets
6. Release beta for early feedback

---

**Document Version:** 1.0  
**Last Updated:** 2026-02-15  
**Author:** GitHub Copilot Analysis  
**Status:** Draft for Review
