using LanShare.Configuration;
using LanShare.Infrastructure;
using LanShare.Services;

namespace LanShare.Api;

public static class StatusEndpoints
{
    public static void MapStatusEndpoints(this WebApplication app) =>
        app.MapGet("/api/status", (HttpContext context, AppSettings settings, AuthService auth) => Results.Ok(new
        {
            name = "局域网文件站", version = typeof(StatusEndpoints).Assembly.GetName().Version?.ToString(3),
            requiresPassword = !string.IsNullOrEmpty(settings.AccessPassword),
            authenticated = auth.IsAuthorized(context), admin = auth.IsAdmin(context), maxFileSize = settings.MaxFileSizeBytes,
            addresses = NetworkAddresses.GetLocal(settings.Port)
        }));
}
