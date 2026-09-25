using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Gmail.v1;
using Google.Apis.Http;
using Google.Apis.Services;
using Google.Apis.Util;

namespace Gplus.Gmail;

public sealed class NotAuthorizedException(string message) : Exception(message);

/// <summary>Builds an authenticated <see cref="GmailService"/> from the stored refresh token.</summary>
public static class GmailConnector
{
    private const string UserKey = "gmail-user";

    /// <summary>gmail.modify: read, label, trash and report spam. It cannot permanently delete
    /// mail or send anything.</summary>
    private static readonly string[] Scopes = [GmailService.Scope.GmailModify];

    private static ClientSecrets LoadSecrets()
    {
        if (!File.Exists(AppPaths.ClientSecret))
            throw new NotAuthorizedException(
                $"No OAuth client file at {AppPaths.ClientSecret}. Run:  Gplus authorize <path-to-client_secret.json>");
        using var s = File.OpenRead(AppPaths.ClientSecret);
        return GoogleClientSecrets.FromStream(s).Secrets;
    }

    /// <summary>Interactive: opens the browser for Google consent and stores the token.</summary>
    public static async Task<string> AuthorizeInteractiveAsync(string? clientSecretPath, CancellationToken ct)
    {
        AppPaths.EnsureDataDir();
        if (clientSecretPath is not null)
        {
            // Validate before copying so a wrong file doesn't overwrite a good one.
            using (var s = File.OpenRead(clientSecretPath)) _ = GoogleClientSecrets.FromStream(s).Secrets;
            File.Copy(Path.GetFullPath(clientSecretPath), AppPaths.ClientSecret, overwrite: true);
        }

        var store = new DpapiDataStore(AppPaths.TokenDir);
        await store.ClearAsync(); // force a fresh consent so the account can be switched
        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(LoadSecrets(), Scopes, UserKey, ct, store);

        using var svc = Create(credential);
        var profile = await svc.Users.GetProfile("me").ExecuteAsync(ct);
        return profile.EmailAddress;
    }

    /// <summary>Non-interactive (service): uses the stored token, refreshing it as needed.</summary>
    public static async Task<GmailService> ConnectAsync(CancellationToken ct)
    {
        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = LoadSecrets(),
            Scopes = Scopes,
            DataStore = new DpapiDataStore(AppPaths.TokenDir),
        });
        var token = await flow.LoadTokenAsync(UserKey, ct);
        if (token?.RefreshToken is null)
            throw new NotAuthorizedException("Gmail is not authorized yet. Run:  Gplus authorize <path-to-client_secret.json>");
        return Create(new UserCredential(flow, UserKey, token));
    }

    private static GmailService Create(UserCredential credential)
    {
        var svc = new GmailService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "Gplus",
            // Our own backoff below replaces the default (which only retries 5xx).
            DefaultExponentialBackOffPolicy = ExponentialBackOffPolicy.None,
        });
        var handler = svc.HttpClient.MessageHandler;
        handler.NumTries = 9; // default 3 would stop backing off after ~3s; quota windows are per minute
        handler.AddExecuteInterceptor(Pacer.Instance);
        // Gmail signals throttling with 429, or 403 "rateLimitExceeded"/"userRateLimitExceeded".
        handler.AddUnsuccessfulResponseHandler(new BackOffHandler(
            new BackOffHandler.Initializer(new ExponentialBackOff(TimeSpan.FromSeconds(1), 8))
            {
                MaxTimeSpan = TimeSpan.FromSeconds(64),
                HandleUnsuccessfulResponseFunc = r => (int)r.StatusCode is 429 or >= 500 ||
                    (r.StatusCode == System.Net.HttpStatusCode.Forbidden && IsRateLimit(r)),
            }));
        return svc;
    }

    private static bool IsRateLimit(HttpResponseMessage r)
    {
        try
        {
            // Buffered by the Google client, so reading it here doesn't consume it.
            var body = r.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return body.Contains("RateLimitExceeded", StringComparison.OrdinalIgnoreCase)
                || body.Contains("Quota exceeded", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>
    /// Spaces every Gmail request (all threads share it) to stay under the per-user limit of
    /// 250 quota units/second — a message fetch costs 5 units, so ~50/s is the ceiling; we use 25/s.
    /// </summary>
    private sealed class Pacer : Google.Apis.Http.IHttpExecuteInterceptor
    {
        public static readonly Pacer Instance = new();
        private static readonly TimeSpan Gap = TimeSpan.FromMilliseconds(40);
        private readonly SemaphoreSlim _gate = new(1, 1);
        private DateTime _next = DateTime.MinValue;

        public async Task InterceptAsync(HttpRequestMessage request, CancellationToken ct)
        {
            await _gate.WaitAsync(ct);
            try
            {
                var wait = _next - DateTime.UtcNow;
                if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
                _next = DateTime.UtcNow + Gap;
            }
            finally { _gate.Release(); }
        }
    }
}
