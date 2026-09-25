using System.Net.Mail;
using System.Text.RegularExpressions;

namespace Gplus.Mail;

public static partial class Addresses
{
    /// <summary>Splits a From-style header into (display name, address).</summary>
    public static (string Name, string Address) Parse(string header)
    {
        if (string.IsNullOrWhiteSpace(header)) return ("", "");
        try
        {
            var a = new MailAddress(header.Trim());
            return (a.DisplayName.Trim().Trim('"', '<', '>', ' '), a.Address.ToLowerInvariant());
        }
        catch (FormatException)
        {
            // Spam often carries malformed headers; fall back to grabbing anything address-shaped.
            var m = AddressRegex().Match(header);
            var addr = m.Success ? m.Value.ToLowerInvariant() : "";
            var name = m.Success ? header[..m.Index].Trim().Trim('<', '"', ' ') : header.Trim();
            return (name, addr);
        }
    }

    /// <summary>All addresses in a To/Cc style list.</summary>
    public static IEnumerable<string> ParseList(string header) =>
        string.IsNullOrWhiteSpace(header)
            ? []
            : AddressRegex().Matches(header).Select(m => m.Value.ToLowerInvariant());

    public static string DomainOf(string address)
    {
        var at = address.LastIndexOf('@');
        return at < 0 ? "" : address[(at + 1)..].Trim().TrimEnd('>').ToLowerInvariant();
    }

    /// <summary>
    /// Canonical form used for "is this the same mailbox" checks. Gmail ignores dots and +tags in
    /// the local part, and googlemail.com is gmail.com.
    /// </summary>
    public static string Normalize(string address)
    {
        address = address.Trim().ToLowerInvariant();
        var at = address.LastIndexOf('@');
        if (at < 0) return address;
        var local = address[..at];
        var domain = address[(at + 1)..];
        if (domain == "googlemail.com") domain = "gmail.com";
        var plus = local.IndexOf('+');
        if (plus >= 0) local = local[..plus];
        if (domain == "gmail.com") local = local.Replace(".", "");
        return local + "@" + domain;
    }

    /// <summary>True when <paramref name="domain"/> is <paramref name="root"/> or a subdomain of it.</summary>
    public static bool DomainMatches(string domain, string root)
    {
        root = root.Trim().TrimStart('*', '.', '@').ToLowerInvariant();
        return root.Length > 0 && (domain == root || domain.EndsWith("." + root, StringComparison.Ordinal));
    }

    [GeneratedRegex(@"[A-Za-z0-9._%+\-=']+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}")]
    private static partial Regex AddressRegex();
}
