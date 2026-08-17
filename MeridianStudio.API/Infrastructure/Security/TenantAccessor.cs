using System.Security.Claims;

namespace MeridianStudio.API.Infrastructure.Security;

/// <summary>
/// Reads tenant/user from the authenticated principal, falling back to <c>Auth:DevTenantId</c>
/// when auth is disabled or the request is anonymous. Register scoped.
/// </summary>
public sealed class TenantAccessor(IHttpContextAccessor http, IConfiguration config) : ITenantAccessor
{
    private string DevTenant => config["Auth:DevTenantId"] ?? "local-dev";

    public string TenantId
    {
        get
        {
            var user = http.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated == true)
            {
                // Common tenant claim names; fall back to dev tenant if none present.
                return user.FindFirst("tid")?.Value
                    ?? user.FindFirst("tenant")?.Value
                    ?? user.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value
                    ?? DevTenant;
            }
            return DevTenant;
        }
    }

    public string? UserId
    {
        get
        {
            var user = http.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true) return null;
            return user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? user.FindFirst("sub")?.Value
                ?? user.FindFirst(ClaimTypes.Email)?.Value;
        }
    }
}
