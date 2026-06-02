namespace EventStoreCore;

internal sealed class DbSchedulerEventApplication
{
    public string ProviderName { get; set; } = null!;

    public string RegistrationName { get; set; } = null!;

    public Guid TenantId { get; set; }

    public Guid EventId { get; set; }

    public Guid ClaimId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }
}
