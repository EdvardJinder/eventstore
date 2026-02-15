// ============================================================================
// CURRENT IMPLEMENTATION (v1.x)
// ============================================================================
// This file demonstrates how stream IDs and stream types work in the current
// version of EventStoreCore.
// ============================================================================

using EventStoreCore.Abstractions;

namespace EventStoreCore.Examples.Current;

/// <summary>
/// Example 1: Simple stream with GUID ID and no stream type
/// </summary>
public class SimpleStreamExample
{
    public async Task CreateOrderStream(IEventStore eventStore)
    {
        // Stream identity: (TenantId=Empty, Id=guid, StreamType="")
        var orderId = Guid.NewGuid();
        
        eventStore.StartStream(orderId, events: new[]
        {
            new OrderCreated(orderId, "Customer123"),
            new OrderItemAdded(orderId, "Product456", quantity: 2)
        });
        
        // Fetch the stream
        var stream = await eventStore.FetchForReadingAsync(orderId);
        
        Console.WriteLine($"Stream ID: {stream.Id}");
        Console.WriteLine($"Stream Type: (empty)");
        Console.WriteLine($"Events: {stream.Events.Count}");
    }
}

/// <summary>
/// Example 2: Multiple streams with same ID but different types
/// This is the primary use case for the StreamType parameter
/// </summary>
public class MultipleStreamTypesExample
{
    public async Task CreateDocumentStreams(IEventStore eventStore)
    {
        var documentId = Guid.NewGuid();
        
        // Create lifecycle stream
        // Stream identity: (TenantId=Empty, Id=documentId, StreamType="document-lifecycle")
        eventStore.StartStream("document-lifecycle", documentId, events: new[]
        {
            new DocumentCreated(documentId),
            new DocumentPublished(documentId)
        });
        
        // Create analysis stream (same ID, different type)
        // Stream identity: (TenantId=Empty, Id=documentId, StreamType="document-analysis")
        eventStore.StartStream("document-analysis", documentId, events: new[]
        {
            new AnalysisStarted(documentId),
            new AnalysisCompleted(documentId, score: 0.95)
        });
        
        // Fetch specific stream types
        var lifecycleStream = await eventStore.FetchForReadingAsync("document-lifecycle", documentId);
        var analysisStream = await eventStore.FetchForReadingAsync("document-analysis", documentId);
        
        Console.WriteLine($"Lifecycle stream - ID: {lifecycleStream.Id}, Events: {lifecycleStream.Events.Count}");
        Console.WriteLine($"Analysis stream - ID: {analysisStream.Id}, Events: {analysisStream.Events.Count}");
        
        // Note: Both streams have the same ID (Guid) but are separate entities
        // in the database due to different StreamType values
    }
}

/// <summary>
/// Example 3: Multi-tenant scenarios
/// </summary>
public class MultiTenantExample
{
    public async Task CreateTenantStreams(IEventStore eventStore)
    {
        var orderId = Guid.NewGuid();
        var tenant1Id = Guid.NewGuid();
        var tenant2Id = Guid.NewGuid();
        
        // Tenant 1's order stream
        // Stream identity: (TenantId=tenant1Id, Id=orderId, StreamType="")
        eventStore.StartStream(orderId, tenant1Id, events: new[]
        {
            new OrderCreated(orderId, "Tenant1-Customer")
        });
        
        // Tenant 2 can use the same order ID
        // Stream identity: (TenantId=tenant2Id, Id=orderId, StreamType="")
        eventStore.StartStream(orderId, tenant2Id, events: new[]
        {
            new OrderCreated(orderId, "Tenant2-Customer")
        });
        
        // Fetch tenant-specific streams
        var tenant1Stream = await eventStore.FetchForReadingAsync(orderId, tenant1Id);
        var tenant2Stream = await eventStore.FetchForReadingAsync(orderId, tenant2Id);
        
        // These are completely separate streams
        Console.WriteLine($"Tenant 1 stream: {tenant1Stream.Events.Count} events");
        Console.WriteLine($"Tenant 2 stream: {tenant2Stream.Events.Count} events");
    }
}

/// <summary>
/// Example 4: Current limitations and awkward patterns
/// </summary>
public class CurrentLimitationsExample
{
    public async Task AwkwardPatterns(IEventStore eventStore)
    {
        // PROBLEM 1: Can't use natural business keys directly
        // Business wants: "Order ABC-123"
        // Current system requires: GUID + optional type
        
        var businessOrderNumber = "ABC-123";
        
        // Option A: Create a mapping layer (extra complexity)
        var orderId = await LookupOrderGuid(businessOrderNumber);
        eventStore.StartStream(orderId, events: new[] { new OrderCreated(orderId, businessOrderNumber) });
        
        // Option B: Store business key in StreamType (abuse of concept)
        // This is NOT recommended but shows the limitation
        eventStore.StartStream(businessOrderNumber, Guid.NewGuid(), events: new[] { new OrderCreated(Guid.Empty, businessOrderNumber) });
        
        // PROBLEM 2: Hierarchical relationships are awkward
        // Business wants: "Tenant 123 / Customer CUST-456 / Order ORD-789"
        // Current system: Must create artificial GUIDs and manage relationships externally
        
        var tenantGuid = Guid.NewGuid(); // Can't use "tenant:123" directly
        var customerGuid = Guid.NewGuid(); // Can't use "customer:CUST-456" directly
        var orderGuid = Guid.NewGuid(); // Can't use "order:ORD-789" directly
        
        // PROBLEM 3: API has many overloads due to (streamType, streamId) combinations
        // - FetchForWritingAsync(Guid streamId)
        // - FetchForWritingAsync(string streamType, Guid streamId)
        // - FetchForWritingAsync(Guid streamId, Guid tenantId)
        // - FetchForWritingAsync(string streamType, Guid streamId, Guid tenantId)
        // ... and many more (40+ methods total)
    }
    
    private Task<Guid> LookupOrderGuid(string businessOrderNumber)
    {
        // Simulate external mapping
        return Task.FromResult(Guid.NewGuid());
    }
}

/// <summary>
/// Example 5: Database schema structure
/// </summary>
public class DatabaseSchemaExample
{
    public void ExplainCurrentSchema()
    {
        /*
         * DbStream Table:
         * ---------------
         * PRIMARY KEY: (TenantId, Id, StreamType)
         * 
         * TenantId     | Id                                   | StreamType         | CurrentVersion | CreatedTimestamp
         * -------------|--------------------------------------|--------------------|-----------------|-----------------
         * 00000000-... | 550e8400-e29b-41d4-a716-446655440000 | ""                 | 5              | 2024-01-15
         * 00000000-... | 550e8400-e29b-41d4-a716-446655440000 | "document-lifecycle"| 3             | 2024-01-15
         * 00000000-... | 550e8400-e29b-41d4-a716-446655440000 | "document-analysis" | 2             | 2024-01-15
         * 
         * Note: Same GUID (550e8400...) appears in 3 different streams due to different StreamType values
         * 
         * DbEvent Table:
         * --------------
         * PRIMARY KEY: (TenantId, StreamId, StreamType, Version)
         * 
         * TenantId     | StreamId                             | StreamType          | Version | Type                | Data
         * -------------|--------------------------------------|---------------------|---------|---------------------|-----
         * 00000000-... | 550e8400-e29b-41d4-a716-446655440000 | ""                  | 1       | "OrderCreated"      | {...}
         * 00000000-... | 550e8400-e29b-41d4-a716-446655440000 | ""                  | 2       | "OrderItemAdded"    | {...}
         * 00000000-... | 550e8400-e29b-41d4-a716-446655440000 | "document-lifecycle"| 1       | "DocumentCreated"   | {...}
         * 00000000-... | 550e8400-e29b-41d4-a716-446655440000 | "document-analysis" | 1       | "AnalysisStarted"   | {...}
         */
    }
}

// ============================================================================
// Event Definitions (for examples)
// ============================================================================

public record OrderCreated(Guid OrderId, string CustomerId);
public record OrderItemAdded(Guid OrderId, string ProductId, int quantity);
public record DocumentCreated(Guid DocumentId);
public record DocumentPublished(Guid DocumentId);
public record AnalysisStarted(Guid DocumentId);
public record AnalysisCompleted(Guid DocumentId, double score);
