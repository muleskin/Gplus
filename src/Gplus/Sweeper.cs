using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Gplus.Filtering;
using Gplus.Gmail;
using Gplus.Mail;
using Gplus.State;
using Microsoft.Extensions.Options;

namespace Gplus;

public sealed record SweepOptions(bool Apply, bool UseSeen, int? Max = null, string? Query = null);

public sealed record SweepResult(int Listed, int Examined, int Junk, int Removed);

/// <summary>One pass over the inbox: list candidates → fetch → classify → act → log.</summary>
public sealed class Sweeper(IOptionsMonitor<GplusOptions> options, ILogger<Sweeper> logger) : IDisposable
{
    private GmailService? _gmail;
    private string _me = "";
    private HashSet<string> _myAddresses = [];
    private JunkClassifier? _classifier;
    private DateTime _rulesStamp;
    private string? _labelId;

    private readonly SeenStore _seen = new(AppPaths.Seen);
    private readonly CorrespondentStore _correspondents = new(AppPaths.Correspondents, logger);
    private readonly DecisionLog _decisions = new(AppPaths.LogDir, options.CurrentValue.LogRetentionDays);

    /// <summary>Raised for every judged message (the console "scan" command prints these).</summary>
    public event Action<MailItem, Verdict, string>? Judged;

    public string Account => _me;

    public async Task<SweepResult> SweepAsync(SweepOptions run, CancellationToken ct)
    {
        var opt = options.CurrentValue;
        await EnsureConnectedAsync(ct);
        var classifier = LoadRules();

        if (opt.LearnCorrespondents && _correspondents.IsStale(TimeSpan.FromHours(opt.CorrespondentRefreshHours)))
        {
            try { await _correspondents.RefreshAsync(_gmail!, opt.CorrespondentScanMax, ct); }
            catch (Google.GoogleApiException ex)
            {
                logger.LogWarning("Correspondent list refresh paused ({Message}); progress kept, it resumes next sweep",
                    ex.Error?.Message ?? ex.Message);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not refresh the correspondent list; using the cached one");
            }
        }

        var myAddresses = new HashSet<string>(_myAddresses);
        foreach (var a in opt.MyAddresses.Where(a => a.Contains('@'))) myAddresses.Add(Addresses.Normalize(a));
        var ctx = new ClassifyContext
        {
            MyAddresses = myAddresses,
            MyDomains = opt.MyAddresses.Where(a => !a.Contains('@')).Select(a => a.Trim().TrimStart('@', '*', '.')).ToList(),
            Correspondents = opt.LearnCorrespondents ? _correspondents.Addresses : new HashSet<string>(),
        };

        var ids = await ListAsync(run.Query ?? opt.Query, run.Max ?? opt.MaxPerPass, ct);
        int examined = 0, junk = 0, removed = 0;

        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            if (run.UseSeen && _seen.Contains(id)) continue;

            MailItem item;
            try
            {
                var get = _gmail!.Users.Messages.Get("me", id);
                get.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Full;
                item = GmailMapper.Map(await get.ExecuteAsync(ct));
            }
            catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
            {
                continue; // deleted between list and get
            }
            examined++;

            var verdict = classifier.Classify(item, ctx);
            string action;
            if (!verdict.IsJunk)
            {
                action = "KEPT";
            }
            else
            {
                junk++;
                if (run.Apply)
                {
                    await RemoveAsync(item.Id, opt, ct);
                    removed++;
                    action = "REMOVED:" + opt.Action.ToString().ToUpperInvariant();
                    logger.LogInformation("Removed [{Category} {Score}] {From} — {Subject}",
                        verdict.Category, verdict.Score, item.FromAddress, item.Subject);
                }
                else
                {
                    action = "WOULD-REMOVE";
                }
            }

            // In the service, even dry-run verdicts are remembered so the log isn't flooded with repeats.
            if (run.UseSeen) _seen.Add(item.Id);
            _decisions.Write(item, verdict, action);
            Judged?.Invoke(item, verdict, action);
        }

        if (run.UseSeen) _seen.Save();
        return new SweepResult(ids.Count, examined, junk, removed);
    }

    public void PruneLogs() => _decisions.Prune();

    /// <summary>Drops the connection so the next sweep re-reads the token (after a re-authorize).</summary>
    public void Reset()
    {
        _gmail?.Dispose();
        _gmail = null;
        _labelId = null;
    }

    private async Task EnsureConnectedAsync(CancellationToken ct)
    {
        if (_gmail is not null) return;
        _gmail = await GmailConnector.ConnectAsync(ct);
        _me = (await _gmail.Users.GetProfile("me").ExecuteAsync(ct)).EmailAddress;
        _myAddresses = [Addresses.Normalize(_me)];
        try
        {
            var sendAs = await _gmail.Users.Settings.SendAs.List("me").ExecuteAsync(ct);
            foreach (var a in sendAs.SendAs ?? []) _myAddresses.Add(Addresses.Normalize(a.SendAsEmail));
        }
        catch (Google.GoogleApiException) { /* aliases are a nicety */ }
        logger.LogInformation("Connected to Gmail as {Account}", _me);
    }

    /// <summary>(Re)loads rules.json when it changes. A broken edit keeps the previous rules running.</summary>
    private JunkClassifier LoadRules()
    {
        AppPaths.EnsureRulesFile();
        var path = AppPaths.Rules;
        var stamp = File.GetLastWriteTimeUtc(path);
        if (_classifier is not null && stamp == _rulesStamp) return _classifier;
        try
        {
            var fresh = new JunkClassifier(RuleSet.Load(path));
            if (_classifier is not null)
            {
                _seen.Clear(); // re-judge recent unread mail under the new rules
                logger.LogInformation("Rules reloaded from {Path}", path);
            }
            _classifier = fresh;
            _rulesStamp = stamp;
        }
        catch (Exception ex) when (_classifier is not null && ex is InvalidDataException or System.Text.Json.JsonException)
        {
            logger.LogError("rules.json has an error, still using the previous rules: {Message}", ex.Message);
            _rulesStamp = stamp; // don't re-log every sweep until it's edited again
        }
        return _classifier;
    }

    private async Task<List<string>> ListAsync(string query, int max, CancellationToken ct)
    {
        var ids = new List<string>();
        string? page = null;
        do
        {
            var req = _gmail!.Users.Messages.List("me");
            req.Q = query;
            req.MaxResults = Math.Min(500, max);
            req.PageToken = page;
            var resp = await req.ExecuteAsync(ct);
            ids.AddRange(resp.Messages?.Select(m => m.Id) ?? []);
            page = resp.NextPageToken;
        } while (page is not null && ids.Count < max);
        return ids.Count > max ? ids.GetRange(0, max) : ids;
    }

    private async Task RemoveAsync(string id, GplusOptions opt, CancellationToken ct)
    {
        var label = await EnsureLabelAsync(opt.RemovedLabel, ct);
        var mod = new ModifyMessageRequest { AddLabelIds = [label], RemoveLabelIds = ["INBOX"] };
        if (opt.Action == JunkAction.Spam) mod.AddLabelIds.Add("SPAM");
        await _gmail!.Users.Messages.Modify(mod, "me", id).ExecuteAsync(ct);
        if (opt.Action == JunkAction.Trash)
            await _gmail.Users.Messages.Trash("me", id).ExecuteAsync(ct);
    }

    private async Task<string> EnsureLabelAsync(string name, CancellationToken ct)
    {
        if (_labelId is not null) return _labelId;
        var labels = await _gmail!.Users.Labels.List("me").ExecuteAsync(ct);
        var existing = labels.Labels?.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
        _labelId = existing?.Id ?? (await _gmail.Users.Labels.Create(new Label
        {
            Name = name,
            LabelListVisibility = "labelShow",
            MessageListVisibility = "show",
        }, "me").ExecuteAsync(ct)).Id;
        return _labelId;
    }

    public void Dispose() => _gmail?.Dispose();
}
