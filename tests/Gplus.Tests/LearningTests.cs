using Gplus.Filtering;
using Gplus.Mail;
using Gplus.State;

namespace Gplus.Tests;

public class LearningTests
{
    private static readonly RuleSet Rules = RuleSet.Load(Path.Combine(AppContext.BaseDirectory, "rules.default.json"));

    private static readonly ClassifyContext Ctx = new()
    {
        MyAddresses = new HashSet<string> { Addresses.Normalize("perry.test@gmail.com") },
        MyDomains = ["mydomain.org"],
        Correspondents = new HashSet<string>
        {
            Addresses.Normalize("bob@bobsbikes.com"),
            Addresses.Normalize("jane@gmail.com"),
        },
    };

    private static string? Learn(string address, params string[] handAllow) =>
        SenderLearner.BlockEntryFor(address, Rules, handAllow, Ctx, out _);

    // ---- what gets blocked -------------------------------------------------------------------

    [Fact]
    public void Marketing_domain_is_blocked_whole() =>
        Assert.Equal("deals.somestore.com", Learn("Offers@Deals.SomeStore.com"));

    [Theory]
    [InlineData("spammer123@gmail.com")]
    [InlineData("prize.dept@yahoo.co.uk")]
    [InlineData("winner@hotmail.fr")]
    [InlineData("scam@outlook.com")]
    [InlineData("x@comcast.net")]
    public void Personal_mail_providers_block_only_the_address(string address) =>
        Assert.Equal(address, Learn(address));

    [Fact]
    public void Brand_domains_block_only_the_address() =>
        Assert.Equal("store-news@amazon.com", Learn("store-news@amazon.com"));

    [Fact]
    public void Bulk_platforms_block_only_the_address() =>
        Assert.Equal("bounce-123@sendgrid.net", Learn("bounce-123@sendgrid.net"));

    [Fact]
    public void Domain_of_someone_you_email_blocks_only_the_address() =>
        Assert.Equal("promo@bobsbikes.com", Learn("promo@bobsbikes.com"));

    // ---- what is never learned ---------------------------------------------------------------

    [Theory]
    [InlineData("bob@bobsbikes.com")]   // someone you've emailed
    [InlineData("jane@gmail.com")]
    [InlineData("perry.test@gmail.com")] // you
    [InlineData("me@mydomain.org")]      // your forwarded domain
    [InlineData("")]
    public void People_you_know_are_never_blocked(string address) => Assert.Null(Learn(address));

    [Fact]
    public void Hand_allow_list_wins() => Assert.Null(Learn("news@mybank.com", "mybank.com"));

    // ---- learned.json --------------------------------------------------------------------------

    [Fact]
    public void Learned_entries_merge_into_the_rules_and_classify()
    {
        var learned = new LearnedRules();
        Assert.True(learned.AddBlock("deals.somestore.com", "example"));
        var rules = RuleSet.Load(Path.Combine(AppContext.BaseDirectory, "rules.default.json"));
        learned.MergeInto(rules);

        var v = new JunkClassifier(rules).Classify(new MailItem
        {
            FromAddress = "hello@deals.somestore.com",
            Subject = "Quilts for the season",
        });
        Assert.True(v.IsJunk);
        Assert.Equal("Blocked", v.Category);
    }

    [Fact]
    public void Duplicates_are_not_added()
    {
        var learned = new LearnedRules();
        Assert.True(learned.AddBlock("somestore.com", ""));
        Assert.False(learned.AddBlock("deals.somestore.com", "")); // already covered by the parent domain
        Assert.False(learned.AddBlock("SomeStore.com", ""));
        Assert.Single(learned.Block);
    }

    [Fact]
    public void Not_junk_undoes_learned_blocks_that_match_the_sender()
    {
        var learned = new LearnedRules();
        learned.AddBlock("somestore.com", "");
        learned.AddBlock("other.com", "");

        var removed = learned.RemoveBlocksMatching("receipts@orders.somestore.com");

        Assert.Equal(["somestore.com"], removed);
        Assert.Equal("other.com", Assert.Single(learned.Block).Sender);
    }

    [Fact]
    public void Round_trips_through_the_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gplus-learned-{Guid.NewGuid():N}.json");
        try
        {
            Assert.Empty(LearnedRules.Load(path).Block); // missing file = empty

            var learned = new LearnedRules();
            learned.AddBlock("somestore.com", "offers@somestore.com — 50% off");
            learned.AddAllow("friend@gmail.com", "friend@gmail.com — hi");
            learned.Save(path);

            var back = LearnedRules.Load(path);
            Assert.Equal("somestore.com", Assert.Single(back.Block).Sender);
            Assert.Equal("friend@gmail.com", Assert.Single(back.Allow).Sender);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_broken_file_throws_instead_of_being_treated_as_empty()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gplus-learned-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{ \"Block\": [ oops");
            Assert.ThrowsAny<System.Text.Json.JsonException>(() => LearnedRules.Load(path));
        }
        finally { File.Delete(path); }
    }
}
