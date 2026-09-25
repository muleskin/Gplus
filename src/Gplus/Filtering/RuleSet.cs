using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Gplus.Filtering;

/// <summary>The contents of rules.json. Every number is a score contribution; a message whose
/// total reaches <see cref="Threshold"/> is junk.</summary>
public sealed class RuleSet
{
    public int Threshold { get; set; } = 6;

    /// <summary>Never touched. "someone@x.com", "*@x.com", or a bare domain "x.com" (includes subdomains).</summary>
    public List<string> AllowSenders { get; set; } = [];

    /// <summary>Always removed (unless also allowed). Same syntax as AllowSenders.</summary>
    public List<string> BlockSenders { get; set; } = [];

    public SignalWeights Signals { get; set; } = new();

    /// <summary>Display-name brand → the domains that brand really sends from. A message whose
    /// display name claims the brand but comes from elsewhere scores <see cref="SignalWeights.BrandSpoof"/>.</summary>
    public Dictionary<string, List<string>> Brands { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<Category> Categories { get; set; } = [];

    /// <summary>Subject patterns for mail people actually need (receipts, codes, statements).</summary>
    public PatternGroup Transactional { get; set; } = new();

    // ---- loading ---------------------------------------------------------------------------

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static RuleSet Load(string path) => Parse(File.ReadAllText(path));

    public static RuleSet Parse(string json)
    {
        var rules = JsonSerializer.Deserialize<RuleSet>(json, JsonOpts)
                    ?? throw new InvalidDataException("rules file is empty");
        rules.Compile();
        return rules;
    }

    /// <summary>Compiles every pattern up front so a bad regex fails at load time, not mid-sweep.</summary>
    public void Compile()
    {
        foreach (var c in Categories)
        {
            c.SubjectRegexes = CompileAll(c.Name, c.Subject.Concat(c.Patterns));
            c.BodyRegexes = CompileAll(c.Name, c.Body.Concat(c.Patterns));
        }
        Transactional.Regexes = CompileAll("Transactional", Transactional.Patterns);
    }

    private static List<Regex> CompileAll(string owner, IEnumerable<string> patterns)
    {
        var list = new List<Regex>();
        foreach (var p in patterns.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct())
        {
            try
            {
                list.Add(new Regex(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
                    TimeSpan.FromMilliseconds(200)));
            }
            catch (ArgumentException ex)
            {
                throw new InvalidDataException($"rules.json: bad pattern in '{owner}': {p} ({ex.Message})", ex);
            }
        }
        return list;
    }
}

public sealed class SignalWeights
{
    /// <summary>Has a List-Unsubscribe header (bulk / marketing mail).</summary>
    public int ListUnsubscribe { get; set; } = 2;
    /// <summary>Precedence: bulk / list / junk.</summary>
    public int PrecedenceBulk { get; set; } = 1;
    /// <summary>Sent through a bulk-mail platform (Mailchimp, SendGrid, Klaviyo, ...).</summary>
    public int BulkMailer { get; set; } = 1;
    /// <summary>Gmail filed it under the Promotions tab.</summary>
    public int GmailPromotions { get; set; } = 3;
    public int GmailSocial { get; set; } = 0;
    public int GmailUpdates { get; set; } = 0;
    public int GmailForums { get; set; } = 0;
    /// <summary>Gmail thinks it's important (negative = protective).</summary>
    public int GmailImportant { get; set; } = -2;
    /// <summary>DMARC failed, or SPF failed without a passing DKIM signature.</summary>
    public int AuthFail { get; set; } = 4;
    /// <summary>Display name claims a brand the sending domain doesn't belong to.</summary>
    public int BrandSpoof { get; set; } = 5;
    /// <summary>Reply-To points to a different domain than From.</summary>
    public int ReplyToMismatch { get; set; } = 1;
    /// <summary>Your address isn't in To/Cc (you were BCC'd or it went to a list).</summary>
    public int NotAddressedToMe { get; set; } = 1;
    /// <summary>no-reply / marketing@ / news@ style sender.</summary>
    public int NoReplySender { get; set; } = 1;
    /// <summary>Sent from a marketing subdomain (e.potterybarn.com, email.brand.com, news.x.com).</summary>
    public int MarketingSubdomain { get; set; } = 1;
    /// <summary>Preview text padded with invisible characters (a marketing-template trick).</summary>
    public int PreheaderPadding { get; set; } = 1;
    /// <summary>MOSTLY CAPITALS, "!!!", or emoji-laden subject.</summary>
    public int ShoutySubject { get; set; } = 1;
    /// <summary>15+ links in the HTML.</summary>
    public int ManyLinks { get; set; } = 1;
    /// <summary>A body that's mostly an image with almost no text.</summary>
    public int ImageOnly { get; set; } = 1;
}

public sealed class Category
{
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;

    /// <summary>Score for a match in the subject or sender display name (counted once).</summary>
    public int SubjectWeight { get; set; } = 4;
    /// <summary>Score per distinct pattern matched in the body.</summary>
    public int BodyWeight { get; set; } = 1;
    /// <summary>Most distinct body matches counted for this category.</summary>
    public int MaxBodyHits { get; set; } = 3;
    /// <summary>Score when the sender's domain is in <see cref="SenderDomains"/>.</summary>
    public int SenderWeight { get; set; } = 8;

    public List<string> SenderDomains { get; set; } = [];
    /// <summary>Regexes checked against subject + display name AND body.</summary>
    public List<string> Patterns { get; set; } = [];
    /// <summary>Regexes checked against subject + display name only.</summary>
    public List<string> Subject { get; set; } = [];
    /// <summary>Regexes checked against the body only.</summary>
    public List<string> Body { get; set; } = [];

    [JsonIgnore] internal List<Regex> SubjectRegexes { get; set; } = [];
    [JsonIgnore] internal List<Regex> BodyRegexes { get; set; } = [];
}

public sealed class PatternGroup
{
    public int Weight { get; set; } = -6;
    public List<string> Patterns { get; set; } = [];
    [JsonIgnore] internal List<Regex> Regexes { get; set; } = [];
}
