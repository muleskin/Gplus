using Gplus.Filtering;
using Gplus.Mail;

namespace Gplus.Tests;

public class WordFilterTests
{
    private static int _id;

    private static MailItem Mail(string from, string subject, string body) => new()
    {
        Id = "m" + Interlocked.Increment(ref _id),
        FromAddress = Addresses.Parse(from).Address,
        FromName = Addresses.Parse(from).Name,
        Subject = subject,
        Body = body,
    };

    private static readonly string[] JunkSubjects =
    [
        "Reverse mortgage secrets they don't tell you", "Unlock your home equity today", "Seniors: claim your reverse mortgage",
        "Your home could pay you every month", "Tap into your home equity now", "Final notice: reverse mortgage eligibility",
    ];

    private static readonly string[] GoodSubjects =
    [
        "Minutes from Tuesday's club meeting", "Field day schedule and antenna setup", "Repeater maintenance this weekend",
        "Club net tonight at 8pm", "Photos from the swap meet", "Volunteer sign-up for the hamfest",
    ];

    /// <summary>A filter trained on reverse-mortgage junk vs radio-club mail.</summary>
    private static WordFilter Trained()
    {
        var f = new WordFilter();
        for (int i = 0; i < 30; i++)
        {
            f.Train(Mail($"Offers <news{i}@homeequity-now.com>", JunkSubjects[i % JunkSubjects.Length],
                "Homeowners 62 and older can unlock tax-free cash with a reverse mortgage. No monthly payments. " +
                "See how much your home equity is worth. Unsubscribe here."), spam: true);
            f.Train(Mail($"Club Secretary <sec{i % 3}@bvarc.org>", GoodSubjects[i % GoodSubjects.Length],
                "Hi all, the repeater on 146.94 will be down Saturday for maintenance. Bring your radios to the " +
                "field day site; we will set up the antenna tower at 9am. 73, the club secretary"), spam: false);
        }
        return f;
    }

    [Fact]
    public void Learns_junk_phrases_from_a_new_sender()
    {
        var f = Trained();
        var s = f.Score(Mail("Retire Rich <hello@golden-years-cash.net>", "Is a reverse mortgage right for you?",
            "Homeowners over 62: unlock tax-free cash from your home equity. No monthly payments."));
        Assert.True(s.Probability >= 0.9, $"p={s.Probability}");
        Assert.NotEmpty(s.SpamClues);
    }

    [Fact]
    public void Recognizes_good_mail()
    {
        var f = Trained();
        var s = f.Score(Mail("Net Control <nc@bvarc.org>", "Club net moved to the backup repeater",
            "The club net tonight will use the backup repeater. Field day antenna photos are posted. 73"));
        Assert.True(s.Probability <= 0.1, $"p={s.Probability}");
    }

    [Fact]
    public void Unknown_text_is_neutral() =>
        Assert.Equal(0.5, Trained().Score(Mail("x@unknown.example", "Zyxxq", "qwertz plugh")).Probability);

    [Fact]
    public void Retraining_moves_a_message_to_the_other_side()
    {
        var f = new WordFilter();
        var m = Mail("a@b.com", "hello there", "some words here");
        Assert.True(f.Train(m, spam: true));
        Assert.False(f.Train(m, spam: true)); // no double counting
        Assert.True(f.Train(m, spam: false));
        Assert.Equal((0, 1), (f.SpamCount, f.HamCount));
        Assert.Equal("h", f.TrainedAs(m.Id));
    }

    [Fact]
    public void Tokens_include_phrases_sender_and_see_through_disguises()
    {
        var tokens = WordFilter.Tokenize(Mail("Deals <x@mail.shop.example.com>", "𝐅𝐑𝐄𝐄 gift", "Pre-approved L​O​A​N offer")).ToHashSet();
        Assert.Contains("s:free", tokens);
        Assert.Contains("s:free gift", tokens);
        Assert.Contains("pre-approved loan", tokens);
        Assert.Contains("from:mail.shop.example.com", tokens);
        Assert.Contains("from:example.com", tokens);
        Assert.Contains("n:deals", tokens);
    }

    [Fact]
    public void Long_numbers_are_ignored() =>
        Assert.DoesNotContain(WordFilter.Tokenize(Mail("a@b.com", "Order 1234567890", "")), t => t.Contains("1234567890"));

    [Fact]
    public void Round_trips_through_the_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gplus-words-{Guid.NewGuid():N}.json");
        try
        {
            var f = Trained();
            f.MarkNoHarvest("abc");
            f.Save(path);
            var back = WordFilter.Load(path);
            Assert.Equal((f.SpamCount, f.HamCount, f.TokenCount), (back.SpamCount, back.HamCount, back.TokenCount));
            Assert.True(back.IsNoHarvest("abc"));

            File.WriteAllText(path, "{ broken");
            Assert.Equal(0, WordFilter.Load(path).SpamCount); // a corrupt file starts over, never crashes
        }
        finally { File.Delete(path); }
    }

    // ---- in the classifier -----------------------------------------------------------------------

    private static readonly JunkClassifier Classifier =
        new(RuleSet.Load(Path.Combine(AppContext.BaseDirectory, "rules.default.json")));

    [Fact]
    public void Adds_points_and_explains_itself()
    {
        var m = Mail("Retire Rich <hello@golden-years-cash.net>", "Is a reverse mortgage right for you?",
            "Homeowners over 62: unlock tax-free cash from your home equity. No monthly payments.");
        var without = Classifier.Classify(m, new ClassifyContext());
        var with = Classifier.Classify(m, new ClassifyContext { Words = Trained() });

        Assert.True(with.WordPoints >= 3);
        Assert.Equal(without.Score + with.WordPoints, with.Score);
        Assert.Contains(with.Reasons, r => r.Contains("words") && r.Contains("% junk"));
    }

    [Fact]
    public void Never_overrides_people_you_email()
    {
        var m = Mail("Bob <bob@golden-years-cash.net>", "Reverse mortgage", "unlock your home equity, no monthly payments");
        var ctx = new ClassifyContext
        {
            Words = Trained(),
            Correspondents = new HashSet<string> { "bob@golden-years-cash.net" },
        };
        Assert.False(Classifier.Classify(m, ctx).IsJunk);
    }

    [Fact]
    public void Chi2Q_matches_known_values()
    {
        Assert.Equal(1.0, WordFilter.Chi2Q(0, 4), 6);
        Assert.Equal(Math.Exp(-1), WordFilter.Chi2Q(2, 2), 6);
        Assert.Equal(0.0404, WordFilter.Chi2Q(10, 4), 3); // e^-5 * (1 + 5)
    }
}
