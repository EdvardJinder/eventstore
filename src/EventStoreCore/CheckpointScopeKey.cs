using EventStoreCore.Abstractions;

namespace EventStoreCore;

internal readonly record struct CheckpointScopeKey(CheckpointScope Scope, Guid TenantId)
{
    public static CheckpointScopeKey Global { get; } = new(CheckpointScope.Global, Guid.Empty);

    public static CheckpointScopeKey Tenant(Guid tenantId) => new(CheckpointScope.Tenant, tenantId);

    public bool IsTenant => Scope == CheckpointScope.Tenant;

    public string LockSuffix => IsTenant ? $":tenant:{TenantId:N}" : string.Empty;
}
