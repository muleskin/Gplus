using Google.Apis.Gmail.v1;
using Gplus.Mail;
using Microsoft.Extensions.Logging;

namespace Gplus.State;

/// <summary>
/// Everyone you've sent mail to (To/Cc/Bcc of your Sent folder). Mail from them is never removed —
/// this is the main reason Gplus can be much more aggressive than Gmail without losing real mail.
/// The first build reads up to CorrespondentScanMax sent messages; later refreshes only read new ones.
/// </summary>
public sealed class CorrespondentStore(string path, ILogger logger)
{
    private sealed class Data
    {
        public long LastRefreshUnix { get; set; }
        public HashSet<string> Addresses { get; set; } = [];
        /// <summary>Sent-message ids already read by an unfinished refresh (resume point).</summary>
        public HashSet<string> PendingDone { get; set; } = [];
    }

    private readonly Data _data = JsonFile.LoadOr(path, () => new Data());

    public IReadOnlySet<string> Addresses => _data.Addresses;

    public bool IsStale(TimeSpan maxAge) =>
        DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(_data.LastRefreshUnix) > maxAge;

    public async Task RefreshAsync(GmailService gmail, int scanMax, CancellationToken ct)
    {
        var started = DateTimeOffset.UtcNow;
        var q = "in:sent";
        if (_data.LastRefreshUnix > 0) q += $" after:{_data.LastRefreshUnix - 86400}"; // 1-day overlap

        var ids = new List<string>();
        string? page = null;
        do
        {
            var req = gmail.Users.Messages.List("me");
            req.Q = q;
            req.MaxResults = 500;
            req.PageToken = page;
            var resp = await req.ExecuteAsync(ct);
            ids.AddRange(resp.Messages?.Select(m => m.Id) ?? []);
            page = resp.NextPageToken;
        } while (page is not null && ids.Count < scanMax);
        if (ids.Count > scanMax) ids.RemoveRange(scanMax, ids.Count - scanMax);

        int before = _data.Addresses.Count;
        int done = 0;
        // Sequential and gently paced (~8/s ≈ 2,400 quota units/min) so a first-time build of a
        // large Sent folder stays well inside Gmail's per-minute budget and leaves room for sweeps.
        // Progress is saved as it goes, so an interrupted build resumes instead of starting over.
        try
        {
            await ReadAllAsync();
        }
        catch
        {
            JsonFile.Save(path, _data); // keep partial progress for the resume
            throw;
        }

        _data.PendingDone.Clear();
        _data.LastRefreshUnix = started.ToUnixTimeSeconds();
        JsonFile.Save(path, _data);
        logger.LogInformation("Correspondents: read {Count} sent messages, {New} new addresses, {Total} total",
            done, _data.Addresses.Count - before, _data.Addresses.Count);

        async Task ReadAllAsync()
        {
            foreach (var id in ids)
            {
                ct.ThrowIfCancellationRequested();
                if (_data.PendingDone.Contains(id)) continue;

                var req = gmail.Users.Messages.Get("me", id);
                req.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata;
                req.MetadataHeaders = new Google.Apis.Util.Repeatable<string>(["To", "Cc", "Bcc"]);
                req.Fields = "payload/headers";
                try
                {
                    var msg = await req.ExecuteAsync(ct);
                    foreach (var h in msg.Payload?.Headers ?? [])
                        foreach (var a in Mail.Addresses.ParseList(h.Value ?? ""))
                            _data.Addresses.Add(Mail.Addresses.Normalize(a));
                }
                catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound) { }

                _data.PendingDone.Add(id);
                if (++done % 100 == 0) JsonFile.Save(path, _data);
                await Task.Delay(120, ct);
            }
        }
    }
}
