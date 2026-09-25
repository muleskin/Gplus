using System.Text.RegularExpressions;
using Gplus.Mail;

namespace Gplus.Filtering;

/// <summary>
/// Decides what to block when you put a message in the training label: the sender's whole domain
/// when that's safe, otherwise just the one address, and nothing at all for people you know.
/// </summary>
public static partial class SenderLearner
{
    /// <summary>
    /// The block-list entry to learn for <paramref name="address"/>, or null when nothing should be
    /// learned. <paramref name="why"/> says which, for the log.
    /// </summary>
    /// <param name="handAllow">AllowSenders from rules.json (not the learned ones).</param>
    public static string? BlockEntryFor(string address, RuleSet rules, IEnumerable<string> handAllow,
        ClassifyContext ctx, out string why)
    {
        address = address.Trim().ToLowerInvariant();
        var domain = Addresses.DomainOf(address);
        if (address.Length == 0 || domain.Length == 0) { why = "no sender address"; return null; }
        if (ctx.IsMe(address)) { why = "sent by you"; return null; }
        if (ctx.Correspondents.Contains(Addresses.Normalize(address))) { why = "someone you've emailed"; return null; }
        if (JunkClassifier.MatchesAny(handAllow, address, domain)) { why = "on your allow list in rules.json"; return null; }

        if (IsFreemail(domain))
        {
            why = $"{domain} is a personal-mail provider, so only this address";
            return address;
        }
        if (JunkClassifier.IsBulkPlatform(domain))
        {
            why = $"{domain} is a bulk-mail platform shared by many brands, so only this address";
            return address;
        }
        if (rules.Brands.Values.SelectMany(d => d).FirstOrDefault(b => Addresses.DomainMatches(domain, b)) is { } brand)
        {
            why = $"{brand} also sends mail you may need, so only this address";
            return address;
        }
        if (ctx.Correspondents.Any(c => Addresses.DomainMatches(Addresses.DomainOf(c), domain)))
        {
            why = $"you've emailed people at {domain}, so only this address";
            return address;
        }

        why = "whole domain";
        return domain;
    }

    internal static bool IsFreemail(string domain) =>
        FreemailDomains.Any(d => Addresses.DomainMatches(domain, d)) || FreemailFamily().IsMatch(domain);

    private static readonly string[] FreemailDomains =
    [
        "gmail.com", "googlemail.com", "ymail.com", "rocketmail.com", "outlook.com", "live.com", "msn.com",
        "passport.com", "icloud.com", "me.com", "mac.com", "proton.me", "protonmail.com", "pm.me", "mail.com",
        "email.com", "zoho.com", "zohomail.com", "fastmail.com", "hey.com", "tutanota.com", "tuta.io",
        "mail.ru", "inbox.ru", "list.ru", "bk.ru", "qq.com", "163.com", "126.com", "sina.com", "naver.com",
        "comcast.net", "att.net", "sbcglobal.net", "bellsouth.net", "verizon.net", "cox.net", "charter.net",
        "earthlink.net", "juno.com", "netzero.net", "optonline.net", "frontier.com", "windstream.net",
        "rogers.com", "shaw.ca", "sympatico.ca", "btinternet.com", "sky.com", "virginmedia.com", "web.de", "t-online.de",
    ];

    /// <summary>yahoo.com, yahoo.co.uk, hotmail.fr, gmx.de, aol.com, yandex.ru ...</summary>
    [GeneratedRegex(@"(^|\.)(yahoo|hotmail|aol|gmx|yandex|libero|orange|wanadoo)\.[a-z.]+$", RegexOptions.IgnoreCase)]
    private static partial Regex FreemailFamily();
}
