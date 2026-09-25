using Gplus.Filtering;
using Gplus.Mail;

namespace Gplus.Tests;

public class TextAndAddressTests
{
    [Theory]
    [InlineData("𝐋𝐨𝐚𝐧", "Loan")]
    [InlineData("ＬＯＡＮ", "LOAN")]
    [InlineData("L​o‌a­n", "Loan")]
    [InlineData("Ĺóáń", "Loan")]
    [InlineData("  lots   of\n\nspace ", "lots of space")]
    public void Normalize_defeats_evasion(string input, string expected) =>
        Assert.Equal(expected, TextTools.Normalize(input));

    [Fact]
    public void Html_hidden_text_is_dropped()
    {
        var text = TextTools.HtmlToText("<p>Hello</p><div style=\"display:none\">wedding photos family</div><script>x()</script>");
        Assert.Contains("Hello", text);
        Assert.DoesNotContain("wedding", text);
        Assert.DoesNotContain("x()", text);
    }

    [Theory]
    [InlineData("Perry.Test+shop@googlemail.com", "perrytest@gmail.com")]
    [InlineData("first.last@company.com", "first.last@company.com")]
    public void Gmail_address_normalization(string input, string expected) =>
        Assert.Equal(expected, Addresses.Normalize(input));

    [Theory]
    [InlineData("\"Chase Bank\" <alerts@chase.com>", "Chase Bank", "alerts@chase.com")]
    [InlineData("plain@x.com", "", "plain@x.com")]
    [InlineData("Broken <<spam@evil.top>", "Broken", "spam@evil.top")]
    public void Parses_from_headers(string header, string name, string address)
    {
        var (n, a) = Addresses.Parse(header);
        Assert.Equal(name, n);
        Assert.Equal(address, a);
    }

    [Theory]
    [InlineData("temu.com", "x@mail.temu.com", true)]
    [InlineData("*@temu.com", "x@temu.com", true)]
    [InlineData("temu.com", "x@notemu.com", false)]
    [InlineData("deals*@shop.com", "deals-weekly@shop.com", true)]
    [InlineData("Mom@Example.com", "mom@example.com", true)]
    public void Sender_list_matching(string entry, string address, bool expected) =>
        Assert.Equal(expected, JunkClassifier.MatchesAny([entry], address, Addresses.DomainOf(address)));

    [Theory]
    [InlineData("mx.google.com; spf=softfail; dkim=none", "spf=softfail")]
    [InlineData("mx.google.com; spf=fail; dkim=pass", null)]
    [InlineData("mx.google.com; dmarc=fail", "dmarc=fail")]
    [InlineData("mx.google.com; spf=pass; dkim=pass; dmarc=pass", null)]
    public void Auth_failure_detection(string header, string? expected) =>
        Assert.Equal(expected, JunkClassifier.AuthFailure(header));

    [Fact]
    public void Bad_regex_in_rules_is_reported_at_load()
    {
        var ex = Assert.Throws<InvalidDataException>(() =>
            RuleSet.Parse("""{ "Categories": [ { "Name": "X", "Patterns": [ "(unclosed" ] } ] }"""));
        Assert.Contains("X", ex.Message);
    }
}
