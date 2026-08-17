using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace MeridianStudio.API.Infrastructure.Security;

/// <summary>
/// Configurable authentication. Gated by <c>Auth:Enabled</c> (false in Development, true in prod).
/// When enabled, JWT bearer is registered from the <c>Auth:Jwt</c> section and the API group
/// requires an authenticated caller. When disabled, no auth middleware runs and the tenant falls
/// back to <c>Auth:DevTenantId</c> — frictionless local dev.
/// </summary>
public static class AuthExtensions
{
    public static bool IsAuthEnabled(this IConfiguration config)
        => config.GetValue("Auth:Enabled", false);

    public static IServiceCollection AddMeridianAuth(this IServiceCollection services, IConfiguration config)
    {
        if (!config.IsAuthEnabled())
            return services; // dev: no scheme registered, TenantAccessor uses the dev fallback

        var jwt = config.GetSection("Auth:Jwt");
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = jwt["Authority"];
                options.Audience = jwt["Audience"];
                // In prod behind TLS termination this stays true; expose only for local IdP testing.
                options.RequireHttpsMetadata = jwt.GetValue("RequireHttpsMetadata", true);
            });

        services.AddAuthorization();
        return services;
    }
}
