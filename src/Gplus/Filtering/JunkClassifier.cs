using System.Text.RegularExpressions;
using Gplus.Mail;

namespace Gplus.Filtering;

/// <param name="WordPoints">The part of <paramref name="Score"/> that came from the word filter.</param>
public sealed record Verdict(bool IsJunk, int Score, string Category, string Decision, IReadOnlyList<string> Reasons,
    int WordPoints = 0)
{
    public string ReasonText => string.Join("; ", Reasons);
}

/// <summary>Who "you" are, for the checks that depend on it.</summary>
public sealed class ClassifyContext
{
    public static readonly ClassifyContext Empty = new();

    /// <summary>Normalized addresses of the mailbox owner (see <see cref="Addresses.Normalize"/>).</summary>
    public IReadOnlySet<string> MyAddresses { get; init; } = new HashSet<string>();

    /// <summary>Domains where every address is you (e.g. a personal domain forwarded into Gmail).</summary>
    public IReadOnlyList<string> MyDomains { get; init; } = [];

    public bool IsMe(string address) =>
        address.Length > 0 &&
        (MyAddresses.Contains(Addresses.Normalize(address)) ||
         MyDomains.Any(d => Addresses.DomainMatches(Addresses.DomainOf(address), d)));

    public bool KnowsWhoIAm => MyAddresses.Count > 0 || MyDomains.Count > 0;

    /// <summary>Normalized addresses you've sent mail to. Mail from these is never removed.</summary>
    public IReadOnlySet<string> Correspondents { get; init; } = new HashSet<string>();

    /// <summary>The trained word filter, or null while it has too few examples to trust.</summary>
    public WordFilter? Words { get; init; }
}

/// <summary>
/// Scores a message against <see cref="RuleSet"/>. The order of the hard gates matters:
/// allow-list → starred → block-list → people you write to → score.
/// </summary>
public sealed partial class JunkClassifier(RuleSet rules)
{
    public RuleSet Rules { get; } = rules;

    public Verdict Classify(MailItem m, ClassifyContext? ctx = null)
    {
        ctx ??= ClassifyContext.Empty;
        var from = m.FromAddress;
        var domain = m.FromDomain;

        if (MatchesAny(Rules.AllowSenders, from, domain))
            return Keep("allow-listed sender");
        if (m.HasLabel("STARRED"))
            return Keep("starred");
        if (MatchesAny(Rules.BlockSenders, from, domain))
            return new Verdict(true, Rules.Threshold, "Blocked", "block-listed sender", [$"block-list: {from}"]);
        if (ctx.IsMe(from))
            return Keep("sent by you");
        if (from.Length > 0 && ctx.Correspondents.Contains(Addresses.Normalize(from)))
            return Keep("someone you've emailed");

        var reasons = new List<string>();
        int score = 0;
        void Add(int weight, string why)
        {
            if (weight == 0) return;
            score += weight;
            reasons.Add($"{(weight > 0 ? "+" : "")}{weight} {why}");
        }

        var subject = TextTools.Normalize(m.Subject);
        var name = TextTools.Normalize(m.FromName);
        var body = TextTools.Normalize(m.Body);
        var headline = name.Length > 0 ? subject + " | " + name : subject;

        // ---- categories -------------------------------------------------------------------
        string bestCategory = "";
        int bestCategoryScore = 0;
        foreach (var c in Rules.Categories.Where(c => c.Enabled))
        {
            int before = score;

            if (domain.Length > 0 && c.SenderDomains.FirstOrDefault(d => Addresses.DomainMatches(domain, d)) is { } hitDomain)
                Add(c.SenderWeight, $"{c.Name}: sender {hitDomain}");

            if (FirstMatch(c.SubjectRegexes, headline) is { } sm)
                Add(c.SubjectWeight, $"{c.Name}: subject \"{Clip(sm)}\"");

            int bodyHits = 0;
            foreach (var rx in c.BodyRegexes)
            {
                if (bodyHits >= c.MaxBodyHits) break;
                if (SafeMatch(rx, body) is { } bm)
                {
                    bodyHits++;
                    Add(c.BodyWeight, $"{c.Name}: body \"{Clip(bm)}\"");
                }
            }

            if (score - before > bestCategoryScore)
            {
                bestCategoryScore = score - before;
                bestCategory = c.Name;
            }
        }

        // ---- structural signals -----------------------------------------------------------
        var s = Rules.Signals;
        bool bulk = m.Header("List-Unsubscribe").Length > 0;
        if (bulk) Add(s.ListUnsubscribe, "List-Unsubscribe");
        if (m.Header("Precedence").Trim().ToLowerInvariant() is "bulk" or "list" or "junk")
            Add(s.PrecedenceBulk, "Precedence: " + m.Header("Precedence").Trim());
        if (BulkMailerName(m) is { } esp) Add(s.BulkMailer, "bulk mailer " + esp);

        if (m.HasLabel("CATEGORY_PROMOTIONS")) Add(s.GmailPromotions, "Gmail: Promotions");
        if (m.HasLabel("CATEGORY_SOCIAL")) Add(s.GmailSocial, "Gmail: Social");
        if (m.HasLabel("CATEGORY_UPDATES")) Add(s.GmailUpdates, "Gmail: Updates");
        if (m.HasLabel("CATEGORY_FORUMS")) Add(s.GmailForums, "Gmail: Forums");
        if (m.HasLabel("IMPORTANT")) Add(s.GmailImportant, "Gmail: Important");

        if (AuthFailure(m.Header("Authentication-Results")) is { } auth) Add(s.AuthFail, "auth " + auth);
        if (SpoofedBrand(name, domain) is { } brand) Add(s.BrandSpoof, $"display name claims \"{brand}\" but sent from {domain}");

        var replyDomain = Addresses.DomainOf(m.ReplyToAddress);
        if (replyDomain.Length > 0 && domain.Length > 0 && RootOf(replyDomain) != RootOf(domain))
            Add(s.ReplyToMismatch, $"Reply-To {replyDomain} ≠ From {domain}");

        if (ctx.KnowsWhoIAm && !m.Recipients.Any(ctx.IsMe))
            Add(s.NotAddressedToMe, "not addressed to you");

        if (from.Length > 0 && NoReplyLocal().IsMatch(from[..Math.Max(0, from.IndexOf('@'))]))
            Add(s.NoReplySender, "bulk-style sender address");
        if (MarketingSubdomain().Match(domain) is { Success: true } sub)
            Add(s.MarketingSubdomain, $"marketing subdomain {sub.Value}");

        // Invisible characters padding the inbox preview line — an email-marketing template trick.
        if (PreheaderPadding(m.Body) >= 10) Add(s.PreheaderPadding, "invisible preview padding");

        if (TextTools.UpperRatio(subject) > 0.6 || subject.Contains("!!") || subject.Contains("$$") || TextTools.EmojiCount(subject) >= 2)
            Add(s.ShoutySubject, "shouty subject");
        if (m.LinkCount >= 15) Add(s.ManyLinks, $"{m.LinkCount} links");
        if (m.LinkCount > 0 && body.Length < 120) Add(s.ImageOnly, "image-only body");

        // ---- learned words and phrases -----------------------------------------------------
        int wordPoints = 0;
        if (ctx.Words?.Score(m) is { } ws)
        {
            wordPoints = ws.Probability >= 0.99 ? s.WordsSpam
                : ws.Probability >= 0.90 ? s.WordsLikelySpam
                : ws.Probability <= 0.10 ? s.WordsHam
                : 0;
            var clues = wordPoints > 0 ? ws.SpamClues : ws.HamClues;
            Add(wordPoints, $"words {ws.Probability * 100:0}% junk" +
                            (clues.Count > 0 ? ": " + string.Join(", ", clues.Select(c => $"\"{Clip(c)}\"")) : ""));
        }

        // ---- things people need -----------------------------------------------------------
        if (FirstMatch(Rules.Transactional.Regexes, subject) is { } tm)
            Add(Rules.Transactional.Weight, $"transactional \"{Clip(tm)}\"");

        bool junk = score >= Rules.Threshold;
        var category = bestCategory.Length > 0 ? bestCategory
            : wordPoints > 0 && score - wordPoints < Rules.Threshold ? "Words"
            : bulk ? "Bulk mail" : "Other";
        return new Verdict(junk, score, category, junk ? "score" : "below threshold", reasons, wordPoints);

        Verdict Keep(string why) => new(false, 0, "", why, [why]);
    }

    // ---- helpers ------------------------------------------------------------------------------

    /// <summary>"a@b.com" exact, "*@b.com" / "*.b.com" glob, or bare "b.com" (domain + subdomains).</summary>
    internal static bool MatchesAny(IEnumerable<string> entries, string address, string domain)
    {
        foreach (var raw in entries)
        {
            var e = raw.Trim().ToLowerInvariant();
            if (e.Length == 0) continue;
            if (e.Contains('@'))
            {
                if (e.StartsWith("*@")) { if (Addresses.DomainMatches(domain, e[2..])) return true; }
                else if (e.Contains('*')) { if (Regex.IsMatch(address, "^" + Regex.Escape(e).Replace(@"\*", ".*") + "$")) return true; }
                else if (Addresses.Normalize(e) == Addresses.Normalize(address)) return true;
            }
            else if (domain.Length > 0 && Addresses.DomainMatches(domain, e)) return true;
        }
        return false;
    }

    private static string? FirstMatch(List<Regex> regexes, string text)
    {
        foreach (var rx in regexes)
            if (SafeMatch(rx, text) is { } v) return v;
        return null;
    }

    private static string? SafeMatch(Regex rx, string text)
    {
        if (text.Length == 0) return null;
        try
        {
            var m = rx.Match(text);
            return m.Success ? m.Value : null;
        }
        catch (RegexMatchTimeoutException) { return null; }
    }

    private static string Clip(string s) => s.Length <= 40 ? s : s[..40] + "…";

    /// <summary>Last two labels of a domain ("mail.chase.com" → "chase.com"); good enough to compare senders.</summary>
    internal static string RootOf(string domain)
    {
        var parts = domain.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 2) return domain;
        // co.uk / com.au style second-level suffixes
        return parts[^2].Length <= 3 && parts[^1].Length == 2
            ? string.Join('.', parts[^3..])
            : string.Join('.', parts[^2..]);
    }

    internal static string? AuthFailure(string authResults)
    {
        if (authResults.Length == 0) return null;
        var a = authResults.ToLowerInvariant();
        if (a.Contains("dmarc=fail")) return "dmarc=fail";
        if ((a.Contains("spf=fail") || a.Contains("spf=softfail")) && !a.Contains("dkim=pass"))
            return a.Contains("spf=fail") ? "spf=fail" : "spf=softfail";
        return null;
    }

    private string? SpoofedBrand(string displayName, string domain)
    {
        if (displayName.Length == 0 || domain.Length == 0) return null;
        var lower = displayName.ToLowerInvariant();

        // "service@paypal.com" <x@evil.example> — an address in the display name that isn't the sender's.
        // ("jane@x.com via SomeList" is legit DMARC rewriting by mailing lists, so skip those.)
        var viaList = lower.Contains(" via ");
        foreach (var shown in viaList ? [] : Addresses.ParseList(displayName))
        {
            var shownDomain = Addresses.DomainOf(shown);
            if (shownDomain.Length > 0 && RootOf(shownDomain) != RootOf(domain)) return shown;
        }

        foreach (var (brand, domains) in Rules.Brands)
        {
            // The display name must essentially BE the brand ("PayPal", "Amazon.com Orders",
            // "Apple Support") — "Apple Valley Dental" is not claiming to be Apple.
            var pattern = $@"^\W*{Regex.Escape(brand.ToLowerInvariant())}(\.com)?(\W+{BrandSuffixes})*\W*$";
            if (!Regex.IsMatch(lower, pattern)) continue;
            if (!domains.Any(d => Addresses.DomainMatches(domain, d))) return brand;
        }
        return null;
    }

    private const string BrandSuffixes =
        @"(support|services?|security|team|id|accounts?|billing|customer|care|inc|llc|pay|store|notifications?|alerts?|help|center|" +
        @"online|bank|banking|protection|orders?|delivery|shipping|rewards?|official|us|usa|member|membership|renewal|subscriptions?|no-?reply)";

    // No "X-Mailgun-*": Mailgun is also a common *forwarding* relay (custom domains forwarded into
    // Gmail), which stamps its headers on every message — personal mail included.
    private static readonly string[] BulkHeaderPrefixes =
    [
        "X-SG-EID", "X-MC-User", "X-Mandrill", "X-Campaign", "X-CampaignID", "X-SFMC", "X-Marketo",
        "X-HubSpot", "X-Emarsys", "X-Klaviyo", "X-Braze", "X-Iterable", "X-Mailjet", "X-Sendinblue", "X-Brevo",
        "X-CSA-Complaints", "X-Omnisend", "X-rpcampaign", "X-Listmonk", "X-ConstantContact", "X-Roving",
    ];

    [GeneratedRegex("mailchimp|sendgrid|klaviyo|constant ?contact|hubspot|marketo|salesforce marketing|exacttarget|braze|iterable|mailjet|sendinblue|brevo|omnisend|campaign monitor|aweber|getresponse|activecampaign|convertkit|drip",
        RegexOptions.IgnoreCase)]
    private static partial Regex EspName();

    /// <summary>Domains that belong to bulk-mail platforms rather than to the brand itself.</summary>
    private static readonly string[] BulkSenderDomains =
    [
        "robly.com", "mcsv.net", "rsgsv.net", "mcdlv.net", "mailchimpapp.net", "sendgrid.net", "klaviyomail.com",
        "ccsend.com", "constantcontact.com", "cmail19.com", "cmail20.com", "createsend.com", "hubspotemail.net",
        "hs-email.net", "exacttarget.com", "mktomail.com", "mailgun.org", "sendinblue.com", "brevosend.com",
        "mailjet.com", "aweber.com", "getresponse.com", "acems1.com", "activehosted.com", "convertkit-mail.com",
        "omnisend.com", "e2ma.net", "icontact.com", "benchmarkemail.com", "mailerlite.com", "moosend.com",
    ];

    private static string? BulkMailerName(MailItem m)
    {
        foreach (var name in m.Headers.Keys)
            foreach (var p in BulkHeaderPrefixes)
                if (name.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return name;
        var hit = EspName().Match(m.Header("X-Mailer"));
        if (hit.Success) return hit.Value;
        var domain = m.FromDomain;
        return BulkSenderDomains.FirstOrDefault(d => Addresses.DomainMatches(domain, d));
    }

    internal static bool IsBulkPlatform(string domain) => BulkSenderDomains.Any(d => Addresses.DomainMatches(domain, d));

    internal static int PreheaderPadding(string rawBody)
    {
        int n = 0;
        foreach (var c in rawBody.AsSpan(0, Math.Min(rawBody.Length, 4000)))
            if (c is '͏' or '‌' or '​' or '­' or ' ' or '‍' or '﻿' or '⠀') n++;
        return n;
    }

    [GeneratedRegex(@"^(e|em|email|emails|news|newsletter|newsletters|marketing|promo|promos|promotions|offers|deals|mkt|engage|info|shop|sale|sales|m|t)\.",
        RegexOptions.IgnoreCase)]
    private static partial Regex MarketingSubdomain();

    [GeneratedRegex(@"^(no-?reply|do-?not-?reply|donotreply|marketing|news|newsletters?|offers?|deals?|promos?|promotions?|specials?|sales|rewards|hello|mailer)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex NoReplyLocal();
}
