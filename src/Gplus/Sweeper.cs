using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Gplus.Filtering;
using Gplus.Gmail;
using Gplus.Mail;
using Gplus.State;
using Microsoft.Extensions.Options;

namespace Gplus;

public sealed record SweepOptions(bool Apply, bool UseSeen, int? Max = null, string? Query = null);

public sealed record SweepResult(int Listed, int Examined, int Junk, int Removed, int Trained = 0, int Restored = 0);

/// <summary>One pass over the inbox: list candidates → fetch → classify → act → log.</summary>
public sealed class Sweeper(IOptionsMonitor<GplusOptions> options, ILogger<Sweeper> logger) : IDisposable
{
    private GmailService? _gmail;
    private string _me = "";
    private HashSet<string> _myAddresses = [];
    private JunkClassifier? _classifier;
    private (DateTime Rules, DateTime Learned) _rulesStamp;
    private List<string> _handAllow = [];
    private readonly Dictionary<string, string> _labelIds = new(StringComparer.OrdinalIgnoreCase);

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

        // Training first, so what it learns applies to this sweep (a rules change re-judges recent mail).
        int trained = 0, restored = 0;
        if (run.Apply && opt.LearnFromLabels)
        {
            (trained, restored) = await TrainAsync(opt, ctx, classifier, ct);
            classifier = LoadRules();
        }

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
        return new SweepResult(ids.Count, examined, junk, removed, trained, restored);
    }

    public void PruneLogs() => _decisions.Prune();

    /// <summary>Drops the connection so the next sweep re-reads the token (after a re-authorize).</summary>
    public void Reset()
    {
        _gmail?.Dispose();
        _gmail = null;
        _labelIds.Clear();
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

    /// <summary>(Re)loads rules.json + learned.json when either changes. A broken edit keeps the previous rules running.</summary>
    private JunkClassifier LoadRules()
    {
        AppPaths.EnsureRulesFile();
        var path = AppPaths.Rules;
        var stamp = (File.GetLastWriteTimeUtc(path), File.GetLastWriteTimeUtc(AppPaths.Learned));
        if (_classifier is not null && stamp == _rulesStamp) return _classifier;

        LearnedRules? learned = null;
        try { learned = LearnedRules.Load(AppPaths.Learned); }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException)
        {
            if (_classifier is not null)
            {
                logger.LogError("learned.json has an error, still using the previous rules: {Message}", ex.Message);
                _rulesStamp = stamp;
                return _classifier;
            }
            logger.LogError("learned.json has an error, so learned senders are ignored until it's fixed: {Message}", ex.Message);
        }

        try
        {
            var rules = RuleSet.Load(path);
            var handAllow = rules.AllowSenders.ToList();
            learned?.MergeInto(rules);
            var fresh = new JunkClassifier(rules);
            if (_classifier is not null)
            {
                _seen.Clear(); // re-judge recent unread mail under the new rules
                logger.LogInformation("Rules reloaded ({Rules} + {Learned})", path, AppPaths.Learned);
            }
            _classifier = fresh;
            _handAllow = handAllow;
            _rulesStamp = stamp;
        }
        catch (Exception ex) when (_classifier is not null && ex is InvalidDataException or System.Text.Json.JsonException)
        {
            logger.LogError("rules.json has an error, still using the previous rules: {Message}", ex.Message);
            _rulesStamp = stamp; // don't re-log every sweep until it's edited again
        }
        return _classifier;
    }

    /// <summary>
    /// Handles the two training labels. Gplus-Train: learn a block for the sender, then remove the
    /// message like any junk. Gplus-NotJunk: undo learned blocks for the sender, allow it, and put
    /// the message back in the inbox. Each lesson is saved before Gmail is touched, so an
    /// interrupted sweep just repeats it next time.
    /// </summary>
    private async Task<(int Trained, int Restored)> TrainAsync(GplusOptions opt, ClassifyContext ctx,
        JunkClassifier classifier, CancellationToken ct)
    {
        var trainLabel = await EnsureLabelAsync(opt.TrainLabel, ct);
        var notJunkLabel = await EnsureLabelAsync(opt.NotJunkLabel, ct);
        var trainIds = await ListLabelAsync(trainLabel, ct);
        var notJunkIds = await ListLabelAsync(notJunkLabel, ct);
        if (trainIds.Count + notJunkIds.Count == 0) return (0, 0);

        LearnedRules learned;
        try { learned = LearnedRules.Load(AppPaths.Learned); }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException)
        {
            logger.LogError("learned.json has an error; messages in {Train} and {NotJunk} wait until it's fixed: {Message}",
                opt.TrainLabel, opt.NotJunkLabel, ex.Message);
            return (0, 0);
        }

        int trained = 0, restored = 0;
        foreach (var id in trainIds)
        {
            ct.ThrowIfCancellationRequested();
            if (await GetSummaryAsync(id, ct) is not { } m) continue;

            bool changed = learned.RemoveAllowsMatching(m.FromAddress).Count > 0;
            var entry = SenderLearner.BlockEntryFor(m.FromAddress, classifier.Rules, _handAllow, ctx, out var why);
            string action;
            if (entry is null) action = "TRAINED:NOT-LEARNED";
            else if (learned.AddBlock(entry, $"{m.FromAddress} — {m.Subject}")) { action = "TRAINED:BLOCK " + entry; changed = true; }
            else action = "TRAINED:ALREADY-BLOCKED";
            if (changed) learned.Save(AppPaths.Learned);

            await RemoveAsync(id, opt, ct, alsoRemoveLabel: trainLabel);
            trained++;
            logger.LogInformation("Trained on {From} — {Subject}: {Action} ({Why})", m.FromAddress, m.Subject, action, why);
            _decisions.Write(m, new Verdict(true, 0, "Trained", why, [why]), action);
        }

        foreach (var id in notJunkIds)
        {
            ct.ThrowIfCancellationRequested();
            if (await GetSummaryAsync(id, ct) is not { } m) continue;

            var unblocked = learned.RemoveBlocksMatching(m.FromAddress);
            bool allowed = learned.AddAllow(m.FromAddress, $"{m.FromAddress} — {m.Subject}");
            if (unblocked.Count > 0 || allowed) learned.Save(AppPaths.Learned);

            if (m.HasLabel("TRASH")) await _gmail!.Users.Messages.Untrash("me", id).ExecuteAsync(ct);
            var removedLabel = await EnsureLabelAsync(opt.RemovedLabel, ct);
            await _gmail!.Users.Messages.Modify(new ModifyMessageRequest
            {
                AddLabelIds = ["INBOX"],
                RemoveLabelIds = [notJunkLabel, removedLabel, "SPAM"],
            }, "me", id).ExecuteAsync(ct);
            restored++;

            var why = (allowed ? "allowed " + m.FromAddress : "already allowed") +
                      (unblocked.Count > 0 ? "; removed learned block " + string.Join(", ", unblocked) : "");
            logger.LogInformation("Not junk: {From} — {Subject}: {Why}", m.FromAddress, m.Subject, why);
            _decisions.Write(m, new Verdict(false, 0, "Not junk", why, [why]), "RESTORED");
        }
        return (trained, restored);
    }

    private async Task<List<string>> ListLabelAsync(string labelId, CancellationToken ct)
    {
        var req = _gmail!.Users.Messages.List("me");
        req.LabelIds = labelId;
        req.IncludeSpamTrash = true; // a message labelled while in Trash or Spam still counts
        req.MaxResults = 500;
        var resp = await req.ExecuteAsync(ct);
        return resp.Messages?.Select(m => m.Id).ToList() ?? [];
    }

    /// <summary>Just the sender, subject and labels; null if the message is gone.</summary>
    private async Task<MailItem?> GetSummaryAsync(string id, CancellationToken ct)
    {
        var get = _gmail!.Users.Messages.Get("me", id);
        get.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata;
        get.MetadataHeaders = new Google.Apis.Util.Repeatable<string>(["From", "Subject"]);
        try
        {
            var msg = await get.ExecuteAsync(ct);
            string Header(string n) => msg.Payload?.Headers?
                .FirstOrDefault(h => string.Equals(h.Name, n, StringComparison.OrdinalIgnoreCase))?.Value ?? "";
            var (name, address) = Addresses.Parse(Header("From"));
            return new MailItem
            {
                Id = msg.Id,
                ThreadId = msg.ThreadId ?? "",
                FromName = name,
                FromAddress = address,
                Subject = Header("Subject"),
                Labels = msg.LabelIds?.ToList() ?? [],
            };
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
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

    private async Task RemoveAsync(string id, GplusOptions opt, CancellationToken ct, string? alsoRemoveLabel = null)
    {
        var label = await EnsureLabelAsync(opt.RemovedLabel, ct);
        var mod = new ModifyMessageRequest { AddLabelIds = [label], RemoveLabelIds = ["INBOX"] };
        if (alsoRemoveLabel is not null) mod.RemoveLabelIds.Add(alsoRemoveLabel);
        if (opt.Action == JunkAction.Spam) mod.AddLabelIds.Add("SPAM");
        await _gmail!.Users.Messages.Modify(mod, "me", id).ExecuteAsync(ct);
        if (opt.Action == JunkAction.Trash)
            await _gmail.Users.Messages.Trash("me", id).ExecuteAsync(ct);
    }

    private async Task<string> EnsureLabelAsync(string name, CancellationToken ct)
    {
        if (_labelIds.TryGetValue(name, out var cached)) return cached;
        var labels = await _gmail!.Users.Labels.List("me").ExecuteAsync(ct);
        var existing = labels.Labels?.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
        var id = existing?.Id ?? (await _gmail.Users.Labels.Create(new Label
        {
            Name = name,
            LabelListVisibility = "labelShow",
            MessageListVisibility = "show",
        }, "me").ExecuteAsync(ct)).Id;
        _labelIds[name] = id;
        return id;
    }

    public void Dispose() => _gmail?.Dispose();
}
