// ============================================================================
// PROPOSED IMPLEMENTATION (v2.0)
// ============================================================================
// This file demonstrates how stream IDs would work with string-based IDs
// and removed stream type parameter.
// ============================================================================

using EventStoreCore.Abstractions;

namespace EventStoreCore.Examples.Proposed;

/// <summary>
/// Example 1: Simple stream with string ID (backward compatible with GUID)
/// </summary>
public class SimpleStreamExample
{
    public async Task CreateOrderStream(IEventStore eventStore)
    {
        // Option A: Use GUID string for backward compatibility
        var orderId = Guid.NewGuid().ToString();
        
        eventStore.StartStream(orderId, events: new[]
        {
            new OrderCreated(orderId, "Customer123"),
            new OrderItemAdded(orderId, "Product456", quantity: 2)
        });
        
        var stream = await eventStore.FetchForReadingAsync(orderId);
        
        Console.WriteLine($"Stream ID: {stream.Id}");
        Console.WriteLine($"Events: {stream.Events.Count}");
        
        // Option B: Use semantic string ID
        var semanticStreamId = "order:550e8400-e29b-41d4-a716-446655440000";
        
        eventStore.StartStream(semanticStreamId, events: new[]
        {
            new OrderCreated(semanticStreamId, "Customer456")
        });
        
        var semanticStream = await eventStore.FetchForReadingAsync(semanticStreamId);
        Console.WriteLine($"Semantic Stream ID: {semanticStream.Id}");
    }
}

/// <summary>
/// Example 2: Multiple stream types encoded in the string ID
/// Replaces the old (streamType, guid) pattern
/// </summary>
public class MultipleStreamTypesExample
{
    public async Task CreateDocumentStreams(IEventStore eventStore)
    {
        var documentGuid = Guid.NewGuid();
        
        // Encode stream type in the ID using a delimiter
        var lifecycleStreamId = $"document-lifecycle:{documentGuid}";
        var analysisStreamId = $"document-analysis:{documentGuid}";
        
        // Create lifecycle stream
        eventStore.StartStream(lifecycleStreamId, events: new[]
        {
            new DocumentCreated(lifecycleStreamId),
            new DocumentPublished(lifecycleStreamId)
        });
        
        // Create analysis stream (different stream ID = different stream)
        eventStore.StartStream(analysisStreamId, events: new[]
        {
            new AnalysisStarted(analysisStreamId),
            new AnalysisCompleted(analysisStreamId, score: 0.95)
        });
        
        // Fetch by full stream ID
        var lifecycleStream = await eventStore.FetchForReadingAsync(lifecycleStreamId);
        var analysisStream = await eventStore.FetchForReadingAsync(analysisStreamId);
        
        Console.WriteLine($"Lifecycle stream - ID: {lifecycleStream.Id}, Events: {lifecycleStream.Events.Count}");
        Console.WriteLine($"Analysis stream - ID: {analysisStream.Id}, Events: {analysisStream.Events.Count}");
        
        // Note: The stream "type" is now embedded in the stream ID itself
        // This is more explicit and eliminates the need for a separate parameter
    }
}

/// <summary>
/// Example 3: Natural business keys (major new capability)
/// </summary>
public class NaturalKeysExample
{
    public async Task UseNaturalKeys(IEventStore eventStore)
    {
        // Invoice numbers as stream IDs
        var invoiceStreamId = "invoice:2024-INV-001";
        eventStore.StartStream(invoiceStreamId, events: new[]
        {
            new InvoiceIssued(invoiceStreamId, amount: 1000.00m),
            new InvoicePaymentReceived(invoiceStreamId, amount: 1000.00m)
        });
        
        // Product SKUs as stream IDs
        var productStreamId = "product:SKU-ABC-123";
        eventStore.StartStream(productStreamId, events: new[]
        {
            new ProductCreated(productStreamId, name: "Widget"),
            new ProductPriceChanged(productStreamId, newPrice: 29.99m)
        });
        
        // Email-based user streams
        var userStreamId = "user:john.doe@example.com";
        eventStore.StartStream(userStreamId, events: new[]
        {
            new UserRegistered(userStreamId, email: "john.doe@example.com"),
            new UserEmailVerified(userStreamId)
        });
        
        // Easy retrieval by natural key
        var invoiceStream = await eventStore.FetchForReadingAsync("invoice:2024-INV-001");
        var productStream = await eventStore.FetchForReadingAsync("product:SKU-ABC-123");
        var userStream = await eventStore.FetchForReadingAsync("user:john.doe@example.com");
        
        Console.WriteLine($"Invoice: {invoiceStream.Events.Count} events");
        Console.WriteLine($"Product: {productStream.Events.Count} events");
        Console.WriteLine($"User: {userStream.Events.Count} events");
    }
}

/// <summary>
/// Example 4: Hierarchical stream IDs (new capability)
/// </summary>
public class HierarchicalStreamExample
{
    public async Task CreateHierarchicalStreams(IEventStore eventStore)
    {
        // Multi-level hierarchy using path-like syntax
        var tenantStreamId = "tenant:123/customer:CUST-456/order:ORD-789";
        
        eventStore.StartStream(tenantStreamId, events: new[]
        {
            new OrderCreated(tenantStreamId, customerId: "CUST-456"),
            new OrderShipped(tenantStreamId)
        });
        
        // Organizational hierarchy
        var teamStreamId = "organization:acme/department:engineering/team:backend";
        
        eventStore.StartStream(teamStreamId, events: new[]
        {
            new TeamCreated(teamStreamId, name: "Backend Team"),
            new TeamMemberAdded(teamStreamId, memberId: "emp-123")
        });
        
        // Shopping cart with session
        var cartStreamId = "session:abc-def-123/cart:shopping";
        
        eventStore.StartStream(cartStreamId, events: new[]
        {
            new CartCreated(cartStreamId),
            new CartItemAdded(cartStreamId, productId: "SKU-456", quantity: 1)
        });
        
        var stream = await eventStore.FetchForReadingAsync(tenantStreamId);
        Console.WriteLine($"Hierarchical stream: {stream.Id}");
        Console.WriteLine($"Events: {stream.Events.Count}");
    }
}

/// <summary>
/// Example 5: Domain-Driven Design aggregate pattern
/// </summary>
public class DddAggregateExample
{
    public async Task CreateAggregateStreams(IEventStore eventStore)
    {
        // DDD pattern: AggregateType/AggregateId
        var orderStreamId = $"Order/{Guid.NewGuid()}";
        var customerStreamId = $"Customer/CUST-{Guid.NewGuid():N}";
        var shoppingCartStreamId = $"ShoppingCart/session-{Guid.NewGuid()}";
        
        eventStore.StartStream(orderStreamId, events: new[]
        {
            new OrderCreated(orderStreamId, customerId: "CUST-123")
        });
        
        eventStore.StartStream(customerStreamId, events: new[]
        {
            new CustomerRegistered(customerStreamId, email: "customer@example.com")
        });
        
        eventStore.StartStream(shoppingCartStreamId, events: new[]
        {
            new CartCreated(shoppingCartStreamId)
        });
        
        // Pattern allows easy querying by aggregate type (using database queries with LIKE)
        // SELECT * FROM Streams WHERE Id LIKE 'Order/%'
    }
}

/// <summary>
/// Example 6: Migration helpers for backward compatibility
/// </summary>
public class MigrationHelperExample
{
    public async Task UseHelpers(IEventStore eventStore)
    {
        // Helper class to ease migration from v1.x to v2.0
        var streamId = StreamIdHelper.FromTypeAndGuid("order-processing", Guid.NewGuid());
        // Result: "order-processing:550e8400-e29b-41d4-a716-446655440000"
        
        eventStore.StartStream(streamId, events: new[]
        {
            new OrderCreated(streamId, customerId: "CUST-123")
        });
        
        // Hierarchical helper
        var hierarchicalId = StreamIdHelper.Hierarchical("tenant:123", "order:456");
        // Result: "tenant:123/order:456"
        
        eventStore.StartStream(hierarchicalId, events: new[]
        {
            new OrderCreated(hierarchicalId, customerId: "CUST-456")
        });
        
        // For backward compatibility with pure GUID
        var guidOnlyId = StreamIdHelper.FromGuid(Guid.NewGuid());
        // Result: "550e8400-e29b-41d4-a716-446655440000"
        
        eventStore.StartStream(guidOnlyId, events: new[]
        {
            new OrderCreated(guidOnlyId, customerId: "CUST-789")
        });
    }
}

/// <summary>
/// Example 7: Simplified API surface
/// </summary>
public class SimplifiedApiExample
{
    public void CompareApiSurface()
    {
        /*
         * BEFORE (v1.x): 40+ method overloads
         * ====================================
         * 
         * // Without StreamType
         * Task<IStream?> FetchForWritingAsync(Guid streamId, ...)
         * Task<IStream?> FetchForWritingAsync(Guid streamId, Guid tenantId, ...)
         * Task<IStream<T>?> FetchForWritingAsync<T>(Guid streamId, ...)
         * Task<IStream<T>?> FetchForWritingAsync<T>(Guid streamId, Guid tenantId, ...)
         * 
         * // With StreamType
         * Task<IStream?> FetchForWritingAsync(string streamType, Guid streamId, ...)
         * Task<IStream?> FetchForWritingAsync(string streamType, Guid streamId, Guid tenantId, ...)
         * Task<IStream<T>?> FetchForWritingAsync<T>(string streamType, Guid streamId, ...)
         * Task<IStream<T>?> FetchForWritingAsync<T>(string streamType, Guid streamId, Guid tenantId, ...)
         * 
         * ... (repeated for StartStream, FetchForReading, FetchForReadingWithVersion, etc.)
         * 
         * 
         * AFTER (v2.0): ~20 method overloads (50% reduction!)
         * ===================================================
         * 
         * Task<IStream?> FetchForWritingAsync(string streamId, ...)
         * Task<IStream?> FetchForWritingAsync(string streamId, Guid tenantId, ...)
         * Task<IStream<T>?> FetchForWritingAsync<T>(string streamId, ...)
         * Task<IStream<T>?> FetchForWritingAsync<T>(string streamId, Guid tenantId, ...)
         * 
         * ... (repeated for StartStream, FetchForReading, FetchForReadingWithVersion, etc.)
         * 
         * BENEFIT: Cleaner IntelliSense, easier to learn, less cognitive load
         */
    }
}

/// <summary>
/// Example 8: Database schema (proposed)
/// </summary>
public class DatabaseSchemaExample
{
    public void ExplainProposedSchema()
    {
        /*
         * DbStream Table:
         * ---------------
         * PRIMARY KEY: (TenantId, Id)
         * 
         * TenantId     | Id                                                | CurrentVersion | CreatedTimestamp
         * -------------|---------------------------------------------------|----------------|-----------------
         * 00000000-... | "550e8400-e29b-41d4-a716-446655440000"            | 5              | 2024-01-15
         * 00000000-... | "document-lifecycle:550e8400-..."                 | 3              | 2024-01-15
         * 00000000-... | "document-analysis:550e8400-..."                  | 2              | 2024-01-15
         * 00000000-... | "invoice:2024-INV-001"                            | 4              | 2024-01-15
         * 00000000-... | "tenant:123/customer:CUST-456/order:ORD-789"      | 8              | 2024-01-15
         * 
         * Note: StreamType column is REMOVED. Type information is encoded in the Id string.
         * 
         * DbEvent Table:
         * --------------
         * PRIMARY KEY: (TenantId, StreamId, Version)
         * 
         * TenantId     | StreamId                                           | Version | Type                | Data
         * -------------|----------------------------------------------------|---------|--------------------|-----
         * 00000000-... | "550e8400-e29b-41d4-a716-446655440000"             | 1       | "OrderCreated"     | {...}
         * 00000000-... | "550e8400-e29b-41d4-a716-446655440000"             | 2       | "OrderItemAdded"   | {...}
         * 00000000-... | "document-lifecycle:550e8400-..."                  | 1       | "DocumentCreated"  | {...}
         * 00000000-... | "document-analysis:550e8400-..."                   | 1       | "AnalysisStarted"  | {...}
         * 00000000-... | "invoice:2024-INV-001"                             | 1       | "InvoiceIssued"    | {...}
         * 00000000-... | "tenant:123/customer:CUST-456/order:ORD-789"       | 1       | "OrderCreated"     | {...}
         * 
         * Note: StreamType column is REMOVED from composite key, simplifying schema.
         */
    }
}

// ============================================================================
// Helper Classes
// ============================================================================

/// <summary>
/// Helper class to ease migration from v1.x to v2.0
/// </summary>
public static class StreamIdHelper
{
    /// <summary>
    /// Converts a GUID to a string stream ID (backward compatible)
    /// </summary>
    public static string FromGuid(Guid id) => id.ToString();
    
    /// <summary>
    /// Combines stream type and GUID into a single string (migration path)
    /// </summary>
    public static string FromTypeAndGuid(string type, Guid id)
    {
        if (string.IsNullOrEmpty(type))
            return id.ToString();
        
        return $"{type}:{id}";
    }
    
    /// <summary>
    /// Creates a hierarchical stream ID from multiple segments
    /// </summary>
    public static string Hierarchical(params string[] segments)
    {
        if (segments == null || segments.Length == 0)
            throw new ArgumentException("At least one segment required", nameof(segments));
        
        return string.Join("/", segments);
    }
    
    /// <summary>
    /// Creates a DDD aggregate-style stream ID
    /// </summary>
    public static string ForAggregate(string aggregateType, string aggregateId)
    {
        if (string.IsNullOrWhiteSpace(aggregateType))
            throw new ArgumentException("Aggregate type is required", nameof(aggregateType));
        if (string.IsNullOrWhiteSpace(aggregateId))
            throw new ArgumentException("Aggregate ID is required", nameof(aggregateId));
        
        return $"{aggregateType}/{aggregateId}";
    }
    
    /// <summary>
    /// Creates a DDD aggregate-style stream ID with GUID
    /// </summary>
    public static string ForAggregate(string aggregateType, Guid aggregateId)
    {
        return ForAggregate(aggregateType, aggregateId.ToString());
    }
}

/// <summary>
/// Validator for stream IDs (security and consistency)
/// </summary>
public class StreamIdValidator
{
    private readonly StreamIdValidationOptions _options;
    
    public StreamIdValidator(StreamIdValidationOptions? options = null)
    {
        _options = options ?? new StreamIdValidationOptions();
    }
    
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
                
        if (!System.Text.RegularExpressions.Regex.IsMatch(streamId, _options.AllowedCharacters))
            throw new ArgumentException(
                "Stream ID contains invalid characters", 
                nameof(streamId));
    }
}

public class StreamIdValidationOptions
{
    public int MaxLength { get; set; } = 255;
    public int MinLength { get; set; } = 1;
    public string AllowedCharacters { get; set; } = @"^[a-zA-Z0-9:/_\-\.@]+$";
}

// ============================================================================
// Event Definitions (for examples)
// ============================================================================

public record OrderCreated(string StreamId, string CustomerId);
public record OrderItemAdded(string StreamId, string ProductId, int quantity);
public record OrderShipped(string StreamId);
public record DocumentCreated(string StreamId);
public record DocumentPublished(string StreamId);
public record AnalysisStarted(string StreamId);
public record AnalysisCompleted(string StreamId, double score);
public record InvoiceIssued(string StreamId, decimal amount);
public record InvoicePaymentReceived(string StreamId, decimal amount);
public record ProductCreated(string StreamId, string name);
public record ProductPriceChanged(string StreamId, decimal newPrice);
public record UserRegistered(string StreamId, string email);
public record UserEmailVerified(string StreamId);
public record TeamCreated(string StreamId, string name);
public record TeamMemberAdded(string StreamId, string memberId);
public record CartCreated(string StreamId);
public record CartItemAdded(string StreamId, string productId, int quantity);
public record CustomerRegistered(string StreamId, string email);
