# Quick Comparison: Current vs Proposed Stream ID Model

## Side-by-Side Comparison

### Basic Stream Creation

#### Current (v1.x)
```csharp
var orderId = Guid.NewGuid();
eventStore.StartStream(orderId, events: [new OrderCreated()]);

var stream = await eventStore.FetchForReadingAsync(orderId);
Console.WriteLine($"Stream ID: {stream.Id}"); // Guid
```

#### Proposed (v2.0)
```csharp
var streamId = "order:550e8400-e29b-41d4-a716-446655440000";
eventStore.StartStream(streamId, events: [new OrderCreated()]);

var stream = await eventStore.FetchForReadingAsync(streamId);
Console.WriteLine($"Stream ID: {stream.Id}"); // string
```

---

### Multiple Stream Types

#### Current (v1.x)
```csharp
var docId = Guid.NewGuid();

// Two parameters: streamType and streamId
eventStore.StartStream("document-lifecycle", docId, events: [...]);
eventStore.StartStream("document-analysis", docId, events: [...]);

var lifecycle = await eventStore.FetchForReadingAsync("document-lifecycle", docId);
var analysis = await eventStore.FetchForReadingAsync("document-analysis", docId);
```

#### Proposed (v2.0)
```csharp
var docId = Guid.NewGuid();

// Type encoded in the stream ID string
eventStore.StartStream($"document-lifecycle:{docId}", events: [...]);
eventStore.StartStream($"document-analysis:{docId}", events: [...]);

var lifecycle = await eventStore.FetchForReadingAsync($"document-lifecycle:{docId}");
var analysis = await eventStore.FetchForReadingAsync($"document-analysis:{docId}");
```

---

### Natural Business Keys

#### Current (v1.x)
```csharp
// NOT EASILY POSSIBLE - must use GUID
var invoiceNumber = "2024-INV-001";
var invoiceGuid = await LookupGuid(invoiceNumber); // Extra mapping needed
eventStore.StartStream(invoiceGuid, events: [...]);

// Or abuse StreamType (not recommended)
eventStore.StartStream(invoiceNumber, Guid.NewGuid(), events: [...]);
```

#### Proposed (v2.0)
```csharp
// DIRECT MAPPING - natural key is the stream ID
var invoiceNumber = "invoice:2024-INV-001";
eventStore.StartStream(invoiceNumber, events: [...]);

var stream = await eventStore.FetchForReadingAsync("invoice:2024-INV-001");
```

---

### Hierarchical Identifiers

#### Current (v1.x)
```csharp
// NOT POSSIBLE - must create artificial GUIDs
var tenantGuid = Guid.NewGuid(); // Can't use "tenant:123"
var customerGuid = Guid.NewGuid(); // Can't use "customer:CUST-456"
var orderGuid = Guid.NewGuid(); // Can't use "order:ORD-789"

// Must manage relationships externally
```

#### Proposed (v2.0)
```csharp
// NATURAL HIERARCHIES
var streamId = "tenant:123/customer:CUST-456/order:ORD-789";
eventStore.StartStream(streamId, events: [...]);

var stream = await eventStore.FetchForReadingAsync(
    "tenant:123/customer:CUST-456/order:ORD-789"
);
```

---

## API Complexity Comparison

### Method Count

| API Category | Current (v1.x) | Proposed (v2.0) | Reduction |
|--------------|----------------|-----------------|-----------|
| FetchForWritingAsync | 8 overloads | 4 overloads | -50% |
| FetchForReadingAsync | 16 overloads | 8 overloads | -50% |
| StartStream | 8 overloads | 4 overloads | -50% |
| **Total** | **~40 methods** | **~20 methods** | **-50%** |

### Example: FetchForWritingAsync

#### Current (v1.x)
```csharp
// 8 different overloads!
Task<IStream?> FetchForWritingAsync(Guid streamId, ...)
Task<IStream?> FetchForWritingAsync(Guid streamId, Guid tenantId, ...)
Task<IStream?> FetchForWritingAsync(string streamType, Guid streamId, ...)
Task<IStream?> FetchForWritingAsync(string streamType, Guid streamId, Guid tenantId, ...)
Task<IStream<T>?> FetchForWritingAsync<T>(Guid streamId, ...)
Task<IStream<T>?> FetchForWritingAsync<T>(Guid streamId, Guid tenantId, ...)
Task<IStream<T>?> FetchForWritingAsync<T>(string streamType, Guid streamId, ...)
Task<IStream<T>?> FetchForWritingAsync<T>(string streamType, Guid streamId, Guid tenantId, ...)
```

#### Proposed (v2.0)
```csharp
// 4 overloads (50% reduction)
Task<IStream?> FetchForWritingAsync(string streamId, ...)
Task<IStream?> FetchForWritingAsync(string streamId, Guid tenantId, ...)
Task<IStream<T>?> FetchForWritingAsync<T>(string streamId, ...)
Task<IStream<T>?> FetchForWritingAsync<T>(string streamId, Guid tenantId, ...)
```

---

## Database Schema Comparison

### DbStream Table

#### Current (v1.x)
```sql
PRIMARY KEY: (TenantId, Id, StreamType)

Columns:
- TenantId: GUID (16 bytes)
- Id: GUID (16 bytes)
- StreamType: VARCHAR (variable)
- CurrentVersion: BIGINT
- CreatedTimestamp: TIMESTAMP
- UpdatedTimestamp: TIMESTAMP
```

#### Proposed (v2.0)
```sql
PRIMARY KEY: (TenantId, Id)

Columns:
- TenantId: GUID (16 bytes)
- Id: VARCHAR(255) (variable, ~60 bytes avg)
- CurrentVersion: BIGINT
- CreatedTimestamp: TIMESTAMP
- UpdatedTimestamp: TIMESTAMP
```

**Change:** StreamType column removed; type info encoded in Id

### DbEvent Table

#### Current (v1.x)
```sql
PRIMARY KEY: (TenantId, StreamId, StreamType, Version)

Columns:
- TenantId: GUID
- StreamId: GUID
- StreamType: VARCHAR
- Version: BIGINT
- ...
```

#### Proposed (v2.0)
```sql
PRIMARY KEY: (TenantId, StreamId, Version)

Columns:
- TenantId: GUID
- StreamId: VARCHAR(255)
- Version: BIGINT
- ...
```

**Change:** StreamType removed from primary key and columns

---

## Performance Impact

### Query Performance (Estimated)

| Workload Size | Current (GUID) | Proposed (String) | Difference |
|---------------|----------------|-------------------|------------|
| 1,000 streams | 0.5 ms | 0.6 ms | +20% |
| 100,000 streams | 2 ms | 2.5 ms | +25% |
| 1,000,000 streams | 5 ms | 7 ms | +40% |
| 10,000,000 streams | 15 ms | 22 ms | +47% |

### Index Size Impact

| Database | GUID Index | String Index | Increase |
|----------|------------|--------------|----------|
| 1M streams | 80 MB | 280 MB | +250% |
| 10M streams | 800 MB | 2.8 GB | +250% |

**Mitigation:**
- Keep stream IDs < 50 characters
- Use shorter prefixes
- Partition large datasets

---

## Migration Complexity

### Code Changes

| Component | Impact | Effort |
|-----------|--------|--------|
| Core abstractions | High | 2 weeks |
| Database schema | High | 1 week + downtime |
| Tests | High | 1-2 weeks |
| Documentation | Medium | 1 week |
| Consumer code | High | Variable (per consumer) |

### Database Migration

```sql
-- Simplified overview
-- 1. Add new string columns
ALTER TABLE Streams ADD COLUMN Id_New VARCHAR(255);

-- 2. Convert data (combine StreamType + Id)
UPDATE Streams SET Id_New = 
  CASE WHEN StreamType = '' THEN Id::TEXT
       ELSE StreamType || ':' || Id::TEXT END;

-- 3. Drop old columns and rebuild constraints
ALTER TABLE Streams DROP COLUMN StreamType;
ALTER TABLE Streams DROP COLUMN Id;
ALTER TABLE Streams RENAME COLUMN Id_New TO Id;

-- 4. Rebuild primary keys and foreign keys
-- (Detailed steps in main analysis document)
```

**Downtime Required:** 4-8 hours for large databases

---

## Naming Convention Examples

### Pattern 1: Type-Prefixed GUID
```
order:550e8400-e29b-41d4-a716-446655440000
document-lifecycle:7c9e6679-7425-40de-944b-e07fc1f90ae7
```

### Pattern 2: Natural Keys
```
invoice:2024-INV-001
product:SKU-ABC-123
user:john.doe@example.com
```

### Pattern 3: Hierarchical
```
tenant:123/customer:CUST-456/order:ORD-789
organization:acme/department:engineering/team:backend
```

### Pattern 4: DDD Aggregates
```
Order/550e8400-e29b-41d4-a716-446655440000
Customer/CUST-123
ShoppingCart/session-abc-def-123
```

---

## Decision Matrix

### ✅ Choose PROPOSED (v2.0) If:
- [ ] You need natural business keys as stream IDs
- [ ] You need hierarchical stream identifiers
- [ ] You want simpler, cleaner API
- [ ] You have <10M streams in production
- [ ] You can afford breaking changes (v2.0 bump)
- [ ] You have 6-8 weeks for development + migration

### ❌ Stay with CURRENT (v1.x) If:
- [ ] You have >10M streams with high query load
- [ ] Your streams are primarily GUID-based
- [ ] You cannot afford breaking changes now
- [ ] Performance is critical (every ms counts)
- [ ] You lack database migration expertise
- [ ] Current system works fine for your use cases

---

## Key Tradeoffs Summary

| Aspect | Current (v1.x) | Proposed (v2.0) | Winner |
|--------|----------------|-----------------|--------|
| **API Simplicity** | 40+ methods | 20 methods | ✅ v2.0 |
| **Expressiveness** | Limited (GUID only) | Unlimited (any string) | ✅ v2.0 |
| **Performance** | Faster (GUID indexing) | Slower (string indexing) | ✅ v1.x |
| **Query Speed** | Baseline | +20-40% slower | ✅ v1.x |
| **Index Size** | Smaller | +250% larger | ✅ v1.x |
| **Natural Keys** | Not supported | Fully supported | ✅ v2.0 |
| **Hierarchies** | Not supported | Fully supported | ✅ v2.0 |
| **Breaking Changes** | None | Major (v2.0) | ✅ v1.x |
| **Migration Effort** | None | 6-8 weeks | ✅ v1.x |
| **Learning Curve** | Steeper | Simpler | ✅ v2.0 |
| **Industry Alignment** | Uncommon | Standard | ✅ v2.0 |

---

## Recommendation: CONDITIONAL APPROVAL

**Proceed if:**
- Expressiveness and domain alignment are high priorities
- You can invest in migration (6-8 weeks)
- Your workload is <10M streams
- You're ready for v2.0 breaking changes

**Do NOT proceed if:**
- You have very large workloads (>10M streams)
- Performance is absolutely critical
- You cannot afford breaking changes now
- Current system meets all your needs

---

## See Also

- **STREAM_ID_ANALYSIS.md** - Comprehensive 40-page analysis
- **analysis-examples/CurrentImplementation.cs** - Code examples for v1.x
- **analysis-examples/ProposedImplementation.cs** - Code examples for v2.0
