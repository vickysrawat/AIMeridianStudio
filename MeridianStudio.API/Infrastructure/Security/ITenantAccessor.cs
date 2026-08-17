namespace MeridianStudio.API.Infrastructure.Security;

/// <summary>
/// Resolves the current tenant + user for artifact scoping. When <c>Auth:Enabled</c> is true it
/// reads the authenticated principal; when auth is off (dev) it returns the configured
/// <c>Auth:DevTenantId</c> so tenant-scoping code paths still run without a login.
/// </summary>
public interface ITenantAccessor
{
    string TenantId { get; }
    string? UserId { get; }
}
