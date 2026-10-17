using System.Net;
using LanShare.Services;

namespace LanShare.Middleware;

public sealed class RequestSecurityMiddleware(RequestDelegate next, ILogger<RequestSecurityMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, AuthService auth)
    {
        var response = context.Response;
        var request = context.Request;
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["X-Frame-Options"] = "DENY";
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
        response.Headers.CacheControl = "no-store";
        var host = request.Host.Host.Trim('[', ']');
        if (!IPAddress.TryParse(host, out _) && !host.Equals("localhost", StringComparison.OrdinalIgnoreCase) &&
            !host.Equals(Dns.GetHostName(), StringComparison.OrdinalIgnoreCase))
        { response.StatusCode = 400; return; }
        if (request.Method is not ("GET" or "HEAD"))
        {
            var origin = request.Headers.Origin.ToString();
            if (request.Headers["X-LanShare-Request"] != "1" || (!string.IsNullOrEmpty(origin) &&
                !origin.Equals($"{request.Scheme}://{request.Host}", StringComparison.OrdinalIgnoreCase)))
            {
                response.StatusCode = 403;
                await response.WriteAsJsonAsync(new { error = "请求来源无效，请刷新页面后重试。" });
                return;
            }
        }
        if (request.Path.StartsWithSegments("/api") && request.Path != "/api/status" &&
            request.Path != "/api/login" && request.Path != "/api/logout" && !auth.IsAuthorized(context))
        {
            response.StatusCode = 401;
            await response.WriteAsJsonAsync(new { error = "请输入访问密码。" });
            return;
        }
        try { await next(context); }
        catch (BadHttpRequestException ex) when (!response.HasStarted)
        {
            response.StatusCode = ex.StatusCode;
            await response.WriteAsJsonAsync(new { error = ex.StatusCode == 413 ? "文件超过大小限制。" : "上传请求无效。" });
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (IOException ex) when (!response.HasStarted)
        {
            logger.LogWarning(ex, "文件操作失败");
            response.StatusCode = 500;
            await response.WriteAsJsonAsync(new { error = "文件操作失败，请检查磁盘空间、文件权限或稍后重试。" });
        }
    }
}
