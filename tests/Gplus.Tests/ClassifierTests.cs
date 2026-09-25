using Gplus.Filtering;
using Gplus.Mail;

namespace Gplus.Tests;

public class ClassifierTests
{
    private const string Me = "perry.test@gmail.com";

    // The shipped rules, exactly as a new install gets them.
    private static readonly JunkClassifier Classifier =
        new(RuleSet.Load(Path.Combine(AppContext.BaseDirectory, "rules.default.json")));

    private static readonly ClassifyContext Ctx = new()
    {
        MyAddresses = new HashSet<string> { Addresses.Normalize(Me) },
        Correspondents = new HashSet<string> { Addresses.Normalize("bob@bobsbikes.com") },
    };

    private static MailItem Mail(string from, string subject, string body = "", string[]? labels = null,
        bool bulk = false, string? auth = null, string to = Me, int links = 0, string replyTo = "")
    {
        var (name, addr) = Addresses.Parse(from);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (bulk) headers["List-Unsubscribe"] = "<mailto:unsub@example.com>";
        if (auth is not null) headers["Authentication-Results"] = auth;
        return new MailItem
        {
            Id = Guid.NewGuid().ToString("N"),
            FromName = name,
            FromAddress = addr,
            ReplyToAddress = replyTo,
            Recipients = [to],
            Subject = subject,
            Body = body,
            Labels = labels ?? ["INBOX", "UNREAD"],
            Headers = headers,
            LinkCount = links,
        };
    }

    private static Verdict Judge(MailItem m) => Classifier.Classify(m, Ctx);

    // ---- junk that must go -----------------------------------------------------------------

    public static TheoryData<string, MailItem> JunkSamples => new()
    {
        { "loan offer", Mail("LendFast Offers <offers@lendfast-now.com>", "You're Pre-Approved for up to $35,000",
            "Get your personal loan today. No credit check required.", bulk: true) },
        { "business funding", Mail("Capital Team <mark@quickcapital.biz>", "Funding approved for your business",
            "Working capital funding available within 24 hours. Low APR.") },
        { "temu direct", Mail("Temu <temu@orders.temu.com>", "🎁 Your $100 coupon bundle is waiting") },
        { "temu via affiliate", Mail("Hot Deals <deals@bargainblast.net>", "TEMU lightning deals up to 90% off",
            "Shop like a billionaire", bulk: true) },
        { "horoscope", Mail("Daily Astro <news@dailyastro.net>", "Your Daily Horoscope for Wednesday",
            "Mercury retrograde brings change. Your sun sign says...", bulk: true) },
        { "psychic", Mail("Madame Rosa <rosa@readings-now.com>", "A free tarot reading is waiting for you") },
        { "weather report", Mail("KXYZ News <alerts@kxyz.com>", "Severe Thunderstorm Warning for your area",
            "Chance of rain 80% tonight. Highs near 91.", bulk: true) },
        { "weather.com", Mail("The Weather Channel <noreply@email.weather.com>", "Your weekend outlook") },
        { "crime report", Mail("Citizen <updates@citizen.com>", "3 incidents near you tonight") },
        { "crime newsletter", Mail("Metro Daily <newsletter@metrodaily.com>", "Police: suspect arrested after downtown burglary",
            "The sheriff's office said the suspect was booked into county jail.", bulk: true) },
        { "retail marketing", Mail("Old Navy <oldnavy@email.oldnavy.com>", "40% off everything - ends tonight!",
            "Shop now. Exclusions apply.", labels: ["INBOX", "UNREAD", "CATEGORY_PROMOTIONS"], bulk: true) },
        { "obfuscated loan", Mail("Finance <info@x9-fin.top>", "𝐏𝐫𝐞-𝐀𝐩𝐩𝐫𝐨𝐯𝐞𝐝: L​o​a​n inside",
            "Get your personal loan today, no credit check.", to: "undisclosed@x9-fin.top") },
        { "paypal phish", Mail("PayPal <service@paypa1-secure.xyz>", "Your account has been suspended",
            "Dear customer, kindly click below to verify your account now.", auth: "mx.google.com; spf=fail; dkim=none; dmarc=fail") },
        { "fake invoice", Mail("Geek Squad Billing <billing.team2931@gmail.com>", "Geek Squad renewal - Order #88213",
            "Your subscription auto-renewed for $399.99. If you did not authorize this purchase, call immediately +1 (888) 555-0199") },
        { "prize", Mail("Rewards Center <no-reply@winners-hub.co>", "Congratulations! You have been selected for a $500 Walmart gift card") },
        { "car warranty", Mail("Auto Protect <notice@autoprotect-direct.com>", "FINAL NOTICE: Your vehicle warranty is expiring") },
        { "miracle pill", Mail("Health Daily <tips@healthdaily-news.com>", "Doctors stunned by this simple trick for belly fat", bulk: true) },
        { "crypto", Mail("Alpha Trader <alerts@moonshot-alpha.io>", "This coin could deliver 100x returns", bulk: true) },
        { "political fundraising", Mail("Team Action PAC <info@actionpac.org>", "FEC deadline: 5x match ends at midnight",
            "Chip in $15 now. Paid for by Action PAC. Not authorized by any candidate.", bulk: true) },
        { "block-list", Mail("SHEIN <noreply@shein.com>", "New in: fall styles") },
    };

    [Theory]
    [MemberData(nameof(JunkSamples))]
    public void Removes_junk(string why, MailItem m)
    {
        var v = Judge(m);
        Assert.True(v.IsJunk, $"{why}: expected junk, got score {v.Score} ({v.ReasonText})");
    }

    // ---- real mail that must survive -------------------------------------------------------

    public static TheoryData<string, MailItem> KeepSamples => new()
    {
        { "friend mentions weather", Mail("Jane Smith <jane.smith@gmail.com>", "Dinner Saturday?",
            "Weather looks nice, want to grill? The forecast says highs near 80.") },
        { "shipping notice", Mail("Amazon.com <shipment-tracking@amazon.com>", "Your Amazon.com order #112-4471 has shipped",
            labels: ["INBOX", "UNREAD", "CATEGORY_UPDATES"]) },
        { "login code", Mail("Acme <no-reply@accounts.acme.com>", "Your verification code is 482113") },
        { "bank statement", Mail("Chase <no.reply.alerts@chase.com>", "Your statement is ready", bulk: true) },
        { "correspondent's sale", Mail("Bob <bob@bobsbikes.com>", "50% off sale this weekend - ends tonight", bulk: true) },
        { "gmail-dot alias of correspondent is still the sender, not recipient", Mail("Colleague <sam@company.com>", "Q3 forecast review",
            "Please review the revenue forecast before Friday.") },
        { "brand-like local business", Mail("Apple Valley Dental <frontdesk@applevalleydental.com>", "Appointment reminder for Tuesday") },
        { "starred", Mail("Weird <x@loans-r-us.biz>", "Pre-approved personal loan", labels: ["INBOX", "UNREAD", "STARRED"]) },
        { "self-sent", Mail("Me <perry.test+notes@gmail.com>", "weather idea for the app") },
        { "doc share", Mail("Chris (via Google Docs) <drive-shares-dm-noreply@google.com>", "Document shared with you: \"Budget 2027\"") },
        { "delivery notice with a promo footer", Mail("TikTok Shop <no-reply@shop-us.tiktok.com>", "Package delivered! 📦",
            "Claim your package at the front door. Get 25% off your next order.", links: 18) },
        { "security alert from the real Google", Mail("Google <no-reply@accounts.google.com>", "Security alert",
            auth: "mx.google.com; dkim=pass; spf=pass; dmarc=pass") },
    };

    [Theory]
    [MemberData(nameof(KeepSamples))]
    public void Keeps_real_mail(string why, MailItem m)
    {
        var v = Judge(m);
        Assert.False(v.IsJunk, $"{why}: expected keep, got score {v.Score} ({v.ReasonText})");
    }

    [Fact]
    public void Allow_list_beats_everything()
    {
        var rules = RuleSet.Load(Path.Combine(AppContext.BaseDirectory, "rules.default.json"));
        rules.AllowSenders.Add("temu.com");
        var v = new JunkClassifier(rules).Classify(Mail("Temu <temu@orders.temu.com>", "Your $100 coupon bundle"), Ctx);
        Assert.False(v.IsJunk);
    }

    [Fact]
    public void Disabled_category_does_not_score()
    {
        var rules = RuleSet.Load(Path.Combine(AppContext.BaseDirectory, "rules.default.json"));
        rules.Categories.Single(c => c.Name.StartsWith("Weather")).Enabled = false;
        var v = new JunkClassifier(rules).Classify(Mail("Wx <a@wx-news.com>", "Severe thunderstorm warning"), Ctx);
        Assert.DoesNotContain(v.Reasons, r => r.Contains("Weather"));
    }

    [Fact]
    public void Forwarded_domain_counts_as_me()
    {
        var ctx = new ClassifyContext { MyAddresses = Ctx.MyAddresses, MyDomains = ["doctorate.cc"] };
        var m = Mail("Shop <deals@shop.example>", "Hello", to: "jdugat@doctorate.cc");
        Assert.DoesNotContain(Classifier.Classify(m, ctx).Reasons, r => r.Contains("not addressed"));
        Assert.Equal("sent by you", Classifier.Classify(Mail("Me <mule@doctorate.cc>", "note"), ctx).Decision);
    }

    [Fact]
    public void Marketing_structure_without_keywords_is_caught()
    {
        // Real-world shape: no junk words in the subject, but every marketing-template tell.
        var m = Mail("PotteryBarn <PotteryBarn@e.potterybarn.com>", "Heritage quilts made for layering",
            "They all ship free! " + string.Concat(Enumerable.Repeat("͏ ", 20)), bulk: true);
        Assert.True(Judge(m).IsJunk, Judge(m).ReasonText);
    }

    [Fact]
    public void Mailgun_forwarding_headers_are_not_a_bulk_signal()
    {
        var m = Mail("Bank of America <onlinebanking@ealerts.bankofamerica.com>", "Hello");
        var withRelay = new MailItem
        {
            FromName = m.FromName, FromAddress = m.FromAddress, Subject = m.Subject, Recipients = m.Recipients,
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["X-Mailgun-Sending-Ip"] = "1.2.3.4" },
        };
        Assert.DoesNotContain(Judge(withRelay).Reasons, r => r.Contains("bulk mailer"));
    }

    [Fact]
    public void Brand_spoof_is_reported()
    {
        var v = Judge(Mail("PayPal <service@paypa1-secure.xyz>", "Hello"));
        Assert.Contains(v.Reasons, r => r.Contains("claims \"paypal\""));
    }
}
