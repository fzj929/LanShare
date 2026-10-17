using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using LanShare.Services;

namespace LanShare.Api;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapPost("/api/login", LoginAsync);
        app.MapPost("/api/logout", (HttpContext context, AuthService auth) =>
        {
            auth.Logout(context);
            return Results.Ok();
        });
    }

    private static async Task<IResult> LoginAsync(HttpContext context, AuthService auth)
    {
        var address = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (auth.IsThrottled(address))
            return Results.Json(new { error = "尝试次数过多，请 5 分钟后重试。" }, statusCode: 429);
        var limit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (limit is { IsReadOnly: false }) limit.MaxRequestBodySize = 4096;
        LoginRequest? body;
        try { body = await context.Request.ReadFromJsonAsync<LoginRequest>(context.RequestAborted); }
        catch (JsonException) { return Results.BadRequest(new { error = "请求格式无效。" }); }
        if (!auth.TryLogin(context, body?.Password ?? "", out var admin))
            return Results.Json(new { error = "密码错误，请重试。" }, statusCode: 401);
        return Results.Ok(new { admin });
    }

    private sealed record LoginRequest(string Password);
}
