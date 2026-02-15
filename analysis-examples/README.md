# Analysis Examples - Stream ID Changes Evaluation

This directory contains code examples and supporting materials for evaluating the proposal to switch from GUID-based stream IDs to string-based stream IDs and remove the StreamType parameter.

## Files in This Directory

### 📄 COMPARISON.md
**Quick reference guide** comparing current (v1.x) and proposed (v2.0) implementations.

**Contents:**
- Side-by-side code examples
- API complexity comparison
- Database schema changes
- Performance impact estimates
- Decision matrix
- Key tradeoffs summary

**Best for:** Getting a quick overview of the changes

---

### 💻 CurrentImplementation.cs
**Code examples** demonstrating how stream IDs work in the **current v1.x implementation**.

**Examples included:**
1. Simple streams with GUID IDs
2. Multiple streams with same ID but different types
3. Multi-tenant scenarios
4. Current limitations and awkward patterns
5. Database schema explanation

**Best for:** Understanding the current system's capabilities and limitations

---

### 💻 ProposedImplementation.cs
**Code examples** demonstrating how stream IDs would work in the **proposed v2.0 implementation**.

**Examples included:**
1. Simple streams with string IDs (backward compatible)
2. Multiple stream types encoded in string ID
3. Natural business keys (invoices, SKUs, emails)
4. Hierarchical stream IDs (tenant/customer/order)
5. DDD aggregate patterns
6. Migration helpers
7. Simplified API surface
8. Helper classes (StreamIdHelper, StreamIdValidator)

**Best for:** Envisioning what the new system would enable

---

### 🗄️ MIGRATION_SCRIPTS.sql
**Database migration scripts** for PostgreSQL and SQL Server.

**Contents:**
- Complete migration script (step-by-step)
- Data conversion logic (StreamType + GUID → string)
- Validation checks (critical for data integrity)
- Rollback procedures
- Performance comparison queries
- Data quality checks
- Index size analysis queries

**Best for:** Understanding migration complexity and technical implementation

---

## Quick Start

### If you're new to this evaluation:
1. Start with **COMPARISON.md** for a high-level overview
2. Review **CurrentImplementation.cs** to understand the baseline
3. Review **ProposedImplementation.cs** to see what's possible
4. Read **MIGRATION_SCRIPTS.sql** to understand migration effort
5. Read the main **../STREAM_ID_ANALYSIS.md** for comprehensive analysis

### If you're evaluating technical feasibility:
1. Start with **MIGRATION_SCRIPTS.sql** to understand database changes
2. Review **ProposedImplementation.cs** to see the new API patterns
3. Review **COMPARISON.md** for performance impact estimates
4. Read the main **../STREAM_ID_ANALYSIS.md** for full details

### If you're a developer using EventStoreCore:
1. Start with **COMPARISON.md** to see how your code would change
2. Review **CurrentImplementation.cs** to find patterns you currently use
3. Review **ProposedImplementation.cs** to see equivalent v2.0 patterns
4. Check the helper classes (StreamIdHelper) for migration assistance

---

## Key Patterns Comparison

### Pattern: Simple Stream

**Current (v1.x):**
```csharp
var orderId = Guid.NewGuid();
eventStore.StartStream(orderId, events: [new OrderCreated()]);
```

**Proposed (v2.0):**
```csharp
var streamId = "order:550e8400-e29b-41d4-a716-446655440000";
eventStore.StartStream(streamId, events: [new OrderCreated()]);
```

---

### Pattern: Multiple Stream Types

**Current (v1.x):**
```csharp
var docId = Guid.NewGuid();
eventStore.StartStream("document-lifecycle", docId, events: [...]);
eventStore.StartStream("document-analysis", docId, events: [...]);
```

**Proposed (v2.0):**
```csharp
var docId = Guid.NewGuid();
eventStore.StartStream($"document-lifecycle:{docId}", events: [...]);
eventStore.StartStream($"document-analysis:{docId}", events: [...]);
```

---

### Pattern: Natural Keys (NEW in v2.0)

**Not easily possible in v1.x**

**Proposed (v2.0):**
```csharp
eventStore.StartStream("invoice:2024-INV-001", events: [...]);
eventStore.StartStream("product:SKU-ABC-123", events: [...]);
eventStore.StartStream("user:john.doe@example.com", events: [...]);
```

---

### Pattern: Hierarchical IDs (NEW in v2.0)

**Not easily possible in v1.x**

**Proposed (v2.0):**
```csharp
eventStore.StartStream("tenant:123/customer:CUST-456/order:ORD-789", events: [...]);
```

---

## Migration Considerations

### Code Changes Required
- [ ] Update all `IEventStore` method signatures (Guid → string)
- [ ] Update `IReadOnlyStream.Id` property (Guid → string)
- [ ] Remove all `streamType` parameters
- [ ] Update all consumer code calling EventStore APIs
- [ ] Update all tests and fixtures
- [ ] Add validation for string stream IDs

### Database Changes Required
- [ ] Add new string columns to Streams and Events tables
- [ ] Convert data: combine StreamType + Id into new string Id
- [ ] Drop old StreamType column
- [ ] Rebuild primary keys and foreign keys
- [ ] Rebuild indexes
- [ ] Update statistics

### Time Estimates
- Development: 6-8 weeks
- Database migration downtime: 4-8 hours (large databases)
- Testing: 2-4 weeks
- Documentation: 1 week

---

## Performance Impact Summary

| Workload Size | Current (GUID) | Proposed (String) | Difference |
|---------------|----------------|-------------------|------------|
| 1K streams    | 0.5ms          | 0.6ms             | +20%       |
| 100K streams  | 2ms            | 2.5ms             | +25%       |
| 1M streams    | 5ms            | 7ms               | +40%       |
| 10M streams   | 15ms           | 22ms              | +47%       |

**Index Size Impact:** +250% (GUID: 80MB → String: 280MB for 1M streams)

---

## Recommendation

✅ **CONDITIONAL APPROVAL**

### Proceed If:
- You need natural business keys or hierarchical identifiers
- You want a simpler, cleaner API
- You have <10M streams in production
- You can invest 6-8 weeks in development + migration
- You're ready for breaking changes (v2.0)

### Do NOT Proceed If:
- You have >10M streams with high query load
- Performance is absolutely critical
- You cannot afford breaking changes now
- Current system meets all your needs
- You lack database migration expertise

---

## Additional Resources

- **Main Analysis:** See `../STREAM_ID_ANALYSIS.md` for comprehensive 40+ page analysis
- **Repository README:** See `../README.md` for project guidelines
- **Current Tests:** See `../tests/EventStoreCore.Tests/EventStoreTests.cs` for existing patterns

---

## Questions?

This analysis covers:
- ✅ Current implementation assessment
- ✅ Detailed tradeoffs (8 advantages, 6 disadvantages)
- ✅ Migration complexity (step-by-step scripts)
- ✅ Alternative approaches (4 options compared)
- ✅ Performance benchmarks (estimated)
- ✅ Naming convention recommendations (4 patterns)
- ✅ Security considerations
- ✅ Code examples (100+ lines)
- ✅ Database migration scripts (complete)
- ✅ Final recommendation with conditions

**Status:** Ready for team review and decision
