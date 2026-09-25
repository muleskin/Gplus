using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Gplus.Mail;

public static partial class TextTools
{
    /// <summary>
    /// Undoes the usual keyword-filter evasion tricks before matching: zero-width / soft-hyphen
    /// characters ("T​E​M​U"), styled Unicode letters ("𝐋𝐨𝐚𝐧", fullwidth "ＬＯＡＮ"),
    /// and combining accents ("Ĺóáń"). Whitespace runs collapse to a single space.
    /// </summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        // NFKC folds mathematical/fullwidth/circled letters to plain ones.
        var s = text.Normalize(NormalizationForm.FormKC);

        var sb = new StringBuilder(s.Length);
        foreach (var ch in s.Normalize(NormalizationForm.FormD))
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat is UnicodeCategory.NonSpacingMark or UnicodeCategory.Format) continue; // accents, ZWSP, ZWJ, soft hyphen, BOM
            if (ch == '͏') continue; // combining grapheme joiner
            sb.Append(ch);
        }
        return Whitespace().Replace(sb.ToString(), " ").Trim();
    }

    /// <summary>Crude but robust HTML → text for keyword matching (not for display).</summary>
    public static string HtmlToText(string html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        var s = ScriptOrStyle().Replace(html, " ");
        s = HiddenBlock().Replace(s, " "); // text hidden with display:none is a classic filter-poisoning trick
        s = BlockTags().Replace(s, "\n");
        s = Tags().Replace(s, " ");
        return WebUtility.HtmlDecode(s);
    }

    public static int CountLinks(string html) =>
        string.IsNullOrEmpty(html) ? 0 : Href().Count(html);

    /// <summary>Share of letters that are upper case (0..1); 0 when there are fewer than 8 letters.</summary>
    public static double UpperRatio(string s)
    {
        int letters = 0, upper = 0;
        foreach (var c in s)
        {
            if (!char.IsLetter(c)) continue;
            letters++;
            if (char.IsUpper(c)) upper++;
        }
        return letters < 8 ? 0 : (double)upper / letters;
    }

    /// <summary>Counts emoji / pictographic symbols (surrogate pairs + misc symbols block).</summary>
    public static int EmojiCount(string s)
    {
        int n = 0;
        for (int i = 0; i < s.Length; i++)
        {
            if (char.IsHighSurrogate(s[i])) { n++; i++; }
            else if (s[i] is >= '☀' and <= '➿') n++;
        }
        return n;
    }

    [GeneratedRegex(@"\s+")] private static partial Regex Whitespace();
    [GeneratedRegex(@"<(script|style|head)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)] private static partial Regex ScriptOrStyle();
    [GeneratedRegex(@"<(div|span|p|td|font)\b[^>]*style\s*=\s*[""'][^""']*(display\s*:\s*none|font-size\s*:\s*0|visibility\s*:\s*hidden)[^""']*[""'][^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)] private static partial Regex HiddenBlock();
    [GeneratedRegex(@"<(br|/p|/div|/tr|/li|/h\d)\b[^>]*>", RegexOptions.IgnoreCase)] private static partial Regex BlockTags();
    [GeneratedRegex(@"<[^>]+>")] private static partial Regex Tags();
    [GeneratedRegex(@"<a\b[^>]*\bhref\s*=", RegexOptions.IgnoreCase)] private static partial Regex Href();
}
