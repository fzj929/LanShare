using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using LanShare.Configuration;

namespace LanShare.Services;

public sealed class AuthService(AppSettings settings)
{
    private const string CookieName = "lanshare-session";
    private readonly ConcurrentDictionary<string, Session> sessions = new();
    private readonly ConcurrentDictionary<string, Attempt> attempts = new();

    public bool IsAuthorized(HttpContext context) => string.IsNullOrEmpty(settings.AccessPassword) || GetSession(context) is not null;
    public bool IsAdmin(HttpContext context) => GetSession(context)?.Admin == true;
    public bool IsThrottled(string address) => attempts.TryGetValue(address, out var attempt) && attempt.Count >= 10 &&
        attempt.Since.AddMinutes(5) > DateTimeOffset.UtcNow;

    public bool TryLogin(HttpContext context, string password, out bool admin)
    {
        var now = DateTimeOffset.UtcNow;
        var address = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        admin = PasswordMatches(password, settings.AdminPassword);
        if (!admin && (string.IsNullOrEmpty(settings.AccessPassword) || !PasswordMatches(password, settings.AccessPassword)))
        {
            attempts.AddOrUpdate(address, new Attempt(now, 1), (_, old) =>
                old.Since.AddMinutes(5) < now ? new(now, 1) : old with { Count = old.Count + 1 });
            return false;
        }
        attempts.TryRemove(address, out _);
        foreach (var expired in sessions.Where(x => x.Value.Expires < now)) sessions.TryRemove(expired.Key, out _);
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        sessions[token] = new Session(admin, now.AddHours(12));
        context.Response.Cookies.Append(CookieName, token, new CookieOptions
        {
            HttpOnly = true, SameSite = SameSiteMode.Strict, MaxAge = TimeSpan.FromHours(12), Path = "/", IsEssential = true
        });
        return true;
    }

    public void Logout(HttpContext context)
    {
        if (context.Request.Cookies.TryGetValue(CookieName, out var token)) sessions.TryRemove(token, out _);
        context.Response.Cookies.Delete(CookieName);
    }

    private Session? GetSession(HttpContext context)
    {
        if (!context.Request.Cookies.TryGetValue(CookieName, out var key) || !sessions.TryGetValue(key, out var session)) return null;
        if (session.Expires > DateTimeOffset.UtcNow) return session;
        sessions.TryRemove(key, out _);
        return null;
    }

    private static bool PasswordMatches(string a, string b) => CryptographicOperations.FixedTimeEquals(
        SHA256.HashData(Encoding.UTF8.GetBytes(a)), SHA256.HashData(Encoding.UTF8.GetBytes(b)));
    private sealed record Session(bool Admin, DateTimeOffset Expires);
    private sealed record Attempt(DateTimeOffset Since, int Count);
}
