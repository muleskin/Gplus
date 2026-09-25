using System.Text.Json;
using System.Text.RegularExpressions;
using Gplus.Mail;

namespace Gplus.Filtering;

/// <summary>How spammy a message's words look, 0 (surely good) .. 1 (surely junk).</summary>
public sealed record WordScore(double Probability, IReadOnlyList<string> SpamClues, IReadOnlyList<string> HamClues);

/// <summary>
/// A statistical word-and-phrase filter (Robinson / SpamBayes chi-squared). It counts how often
/// each word, two-word phrase, sender domain and subject word appears in junk versus good mail,
/// and scores new mail by its most telling tokens. Learns from the training labels and from mail
/// whose fate is already settled; kept in words.json.
/// </summary>
public sealed partial class WordFilter
{
    private sealed class Data
    {
        public int Spam { get; set; }
        public int Ham { get; set; }
        /// <summary>token → [junk count, good count].</summary>
        public Dictionary<string, int[]> Tokens { get; set; } = new(StringComparer.Ordinal);
        /// <summary>message id → "s" or "h", so nothing is counted twice and a retrain can undo.</summary>
        public Dictionary<string, string> Trained { get; set; } = new(StringComparer.Ordinal);
        /// <summary>Removed only because of this filter: never harvested as junk (no feedback loop).</summary>
        public List<string> NoHarvest { get; set; } = [];
    }

    private const double UnknownStrength = 0.45, UnknownProb = 0.5, MinDistance = 0.1;
    private const int MaxClues = 150, MaxTokensPerMessage = 2000, BodyChars = 8000, PruneAbove = 300_000, NoHarvestCap = 5000;

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    private Data _d = new();
    private HashSet<string> _noHarvest = new(StringComparer.Ordinal);

    public int SpamCount => _d.Spam;
    public int HamCount => _d.Ham;
    public int TokenCount => _d.Tokens.Count;

    public bool IsReady(int minSpam, int minHam) => _d.Spam >= minSpam && _d.Ham >= minHam;

    /// <summary>"s", "h" or null.</summary>
    public string? TrainedAs(string id) => _d.Trained.GetValueOrDefault(id);

    public bool IsNoHarvest(string id) => _noHarvest.Contains(id);

    public void MarkNoHarvest(string id)
    {
        if (!_noHarvest.Add(id)) return;
        _d.NoHarvest.Add(id);
        if (_d.NoHarvest.Count > NoHarvestCap)
        {
            foreach (var old in _d.NoHarvest.Take(_d.NoHarvest.Count - NoHarvestCap)) _noHarvest.Remove(old);
            _d.NoHarvest.RemoveRange(0, _d.NoHarvest.Count - NoHarvestCap);
        }
    }

    // ---- persistence ---------------------------------------------------------------------------

    /// <summary>A missing or unreadable file starts an empty filter (it relearns from Gmail).</summary>
    public static WordFilter Load(string path)
    {
        var f = new WordFilter();
        try
        {
            if (File.Exists(path))
                f._d = JsonSerializer.Deserialize<Data>(File.ReadAllText(path), JsonOpts) ?? new Data();
        }
        catch (JsonException) { f._d = new Data(); }
        f._d.Tokens = new Dictionary<string, int[]>(f._d.Tokens.Where(kv => kv.Value is { Length: 2 }), StringComparer.Ordinal);
        f._noHarvest = new HashSet<string>(f._d.NoHarvest, StringComparer.Ordinal);
        return f;
    }

    public void Save(string path)
    {
        if (_d.Tokens.Count > PruneAbove)
            foreach (var k in _d.Tokens.Where(kv => kv.Value[0] + kv.Value[1] <= 1).Select(kv => kv.Key).ToList())
                _d.Tokens.Remove(k);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_d, JsonOpts));
        File.Move(tmp, path, overwrite: true);
    }

    // ---- training ------------------------------------------------------------------------------

    /// <summary>Learns a message as junk or good. A message already learned the other way is moved
    /// over; one already learned this way is left alone (returns false).</summary>
    public bool Train(MailItem m, bool spam)
    {
        var want = spam ? "s" : "h";
        var had = TrainedAs(m.Id);
        if (had == want) return false;
        var tokens = Tokenize(m).ToList();
        if (had is not null) Count(tokens, had == "s", -1);
        Count(tokens, spam, +1);
        if (m.Id.Length > 0) _d.Trained[m.Id] = want;
        return true;
    }

    private void Count(List<string> tokens, bool spam, int delta)
    {
        int i = spam ? 0 : 1;
        if (spam) _d.Spam = Math.Max(0, _d.Spam + delta); else _d.Ham = Math.Max(0, _d.Ham + delta);
        foreach (var t in tokens)
        {
            if (!_d.Tokens.TryGetValue(t, out var c))
            {
                if (delta < 0) continue;
                _d.Tokens[t] = c = new int[2];
            }
            c[i] = Math.Max(0, c[i] + delta);
            if (c[0] == 0 && c[1] == 0) _d.Tokens.Remove(t);
        }
    }

    // ---- scoring -------------------------------------------------------------------------------

    public WordScore Score(MailItem m)
    {
        var clues = new List<(string Token, double F)>();
        foreach (var t in Tokenize(m))
        {
            var f = TokenProb(t);
            if (Math.Abs(f - 0.5) >= MinDistance) clues.Add((t, f));
        }
        if (clues.Count == 0) return new WordScore(0.5, [], []);
        clues = clues.OrderByDescending(c => Math.Abs(c.F - 0.5)).Take(MaxClues).ToList();

        double lnHam = 0, lnSpam = 0;
        foreach (var (_, f) in clues)
        {
            lnHam += Math.Log(f);
            lnSpam += Math.Log(1 - f);
        }
        int n = clues.Count;
        double s = 1 - Chi2Q(-2 * lnSpam, 2 * n);
        double h = 1 - Chi2Q(-2 * lnHam, 2 * n);
        double p = (s - h + 1) / 2;

        return new WordScore(p,
            clues.Where(c => c.F > 0.5).OrderByDescending(c => c.F).Take(3).Select(c => Display(c.Token)).ToList(),
            clues.Where(c => c.F < 0.5).OrderBy(c => c.F).Take(3).Select(c => Display(c.Token)).ToList());
    }

    /// <summary>Robinson's smoothed probability that a message containing this token is junk.</summary>
    private double TokenProb(string token)
    {
        if (!_d.Tokens.TryGetValue(token, out var c)) return UnknownProb;
        double spamRatio = (double)c[0] / Math.Max(1, _d.Spam);
        double hamRatio = (double)c[1] / Math.Max(1, _d.Ham);
        if (spamRatio + hamRatio == 0) return UnknownProb;
        double p = spamRatio / (spamRatio + hamRatio);
        int n = c[0] + c[1];
        return (UnknownStrength * UnknownProb + n * p) / (UnknownStrength + n);
    }

    /// <summary>Probability that chi-squared with <paramref name="v"/> (even) degrees of freedom is ≥ x2.</summary>
    internal static double Chi2Q(double x2, int v)
    {
        double m = x2 / 2, term = Math.Exp(-m), sum = term;
        for (int i = 1; i < v / 2; i++)
        {
            term *= m / i;
            sum += term;
        }
        return Math.Min(sum, 1.0);
    }

    /// <summary>The most telling tokens in the model, for the "words" command.</summary>
    public IEnumerable<(string Token, int Spam, int Ham, double F)> TopClues(bool spam, int count, int minSeen = 3) =>
        _d.Tokens.Where(kv => kv.Value[0] + kv.Value[1] >= minSeen)
            .Select(kv => (Token: kv.Key, Spam: kv.Value[0], Ham: kv.Value[1], F: TokenProb(kv.Key)))
            .OrderBy(x => spam ? -x.F : x.F).ThenByDescending(x => x.Spam + x.Ham)
            .Take(count)
            .Select(x => (Display(x.Token), x.Spam, x.Ham, x.F));

    // ---- tokens --------------------------------------------------------------------------------

    /// <summary>
    /// Distinct tokens: sender domain ("from:"), display-name words ("n:"), subject words and phrases
    /// ("s:"), and body words and two-word phrases. Text is normalized first, so disguised letters
    /// count as the plain word.
    /// </summary>
    internal static IEnumerable<string> Tokenize(MailItem m)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        var domain = m.FromDomain;
        if (domain.Length > 0)
        {
            set.Add("from:" + domain);
            set.Add("from:" + JunkClassifier.RootOf(domain));
        }
        foreach (var w in Words(m.FromName)) set.Add("n:" + w);
        AddWords(set, "s:", m.Subject);
        var body = m.Body.Length > BodyChars ? m.Body[..BodyChars] : m.Body;
        AddWords(set, "", body);
        return set.Count > MaxTokensPerMessage ? set.Take(MaxTokensPerMessage) : set;
    }

    private static void AddWords(HashSet<string> set, string prefix, string text)
    {
        string? prev = null;
        foreach (var w in Words(text))
        {
            set.Add(prefix + w);
            if (prev is not null) set.Add(prefix + prev + " " + w);
            prev = w;
        }
    }

    private static IEnumerable<string> Words(string text)
    {
        foreach (Match m in WordRegex().Matches(TextTools.Normalize(text).ToLowerInvariant()))
        {
            var w = m.Value;
            if (w.Length < 2 || w.Length > 24) continue;
            if (w.Count(char.IsDigit) >= 5) continue; // order numbers, phone numbers, tracking ids
            yield return w;
        }
    }

    private static string Display(string token) =>
        token.StartsWith("s:") ? token[2..] + " (subject)"
        : token.StartsWith("n:") ? token[2..] + " (sender name)"
        : token.StartsWith("from:") ? token[5..] + " (sender)"
        : token;

    [GeneratedRegex(@"[\p{L}\p{N}$]+(?:['.,\-][\p{L}\p{N}]+)*%?")]
    private static partial Regex WordRegex();
}
