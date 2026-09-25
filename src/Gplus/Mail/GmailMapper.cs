using System.Text;
using Google.Apis.Gmail.v1.Data;

namespace Gplus.Mail;

/// <summary>Turns a Gmail API message (format=FULL) into a <see cref="MailItem"/>.</summary>
public static class GmailMapper
{
    private const int MaxBodyChars = 30_000;

    static GmailMapper() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static MailItem Map(Message msg)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in msg.Payload?.Headers ?? [])
        {
            if (h.Name is null) continue;
            // Keep the first occurrence, except Authentication-Results where the top-most one
            // (added by Google's own MX) is also the first — so first-wins is right everywhere.
            headers.TryAdd(h.Name, h.Value ?? "");
        }

        string plain = "", html = "";
        if (msg.Payload is not null) Collect(msg.Payload, ref plain, ref html);

        var body = plain.Length > 0 ? plain : TextTools.HtmlToText(html);
        if (body.Length > MaxBodyChars) body = body[..MaxBodyChars];

        var (fromName, fromAddr) = Addresses.Parse(Get(headers, "From"));
        var (_, replyTo) = Addresses.Parse(Get(headers, "Reply-To"));

        DateTimeOffset? date = msg.InternalDate is long ms ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : null;

        return new MailItem
        {
            Id = msg.Id ?? "",
            ThreadId = msg.ThreadId ?? "",
            FromName = fromName,
            FromAddress = fromAddr,
            ReplyToAddress = replyTo,
            Recipients = Addresses.ParseList(Get(headers, "To"))
                .Concat(Addresses.ParseList(Get(headers, "Cc"))).ToList(),
            Subject = Get(headers, "Subject"),
            Body = body,
            LinkCount = TextTools.CountLinks(html),
            Headers = headers,
            Labels = (IReadOnlyCollection<string>?)msg.LabelIds?.ToList() ?? [],
            Date = date,
        };
    }

    private static string Get(Dictionary<string, string> h, string name) => h.TryGetValue(name, out var v) ? v : "";

    private static void Collect(MessagePart part, ref string plain, ref string html)
    {
        var mime = part.MimeType?.ToLowerInvariant() ?? "";
        if (part.Parts is { Count: > 0 })
        {
            foreach (var p in part.Parts) Collect(p, ref plain, ref html);
            return;
        }
        if (part.Body?.Data is not { Length: > 0 } data) return;
        if (!string.IsNullOrEmpty(part.Filename)) return; // attachment

        if (mime == "text/plain" && plain.Length < MaxBodyChars) plain += Decode(data, part) + "\n";
        else if (mime == "text/html" && html.Length < MaxBodyChars * 4) html += Decode(data, part) + "\n";
    }

    private static string Decode(string base64Url, MessagePart part)
    {
        var s = base64Url.Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
        byte[] bytes;
        try { bytes = Convert.FromBase64String(s); }
        catch (FormatException) { return ""; }
        return CharsetOf(part).GetString(bytes);
    }

    private static Encoding CharsetOf(MessagePart part)
    {
        var ct = part.Headers?.FirstOrDefault(h => string.Equals(h.Name, "Content-Type", StringComparison.OrdinalIgnoreCase))?.Value ?? "";
        var i = ct.IndexOf("charset=", StringComparison.OrdinalIgnoreCase);
        if (i >= 0)
        {
            var cs = ct[(i + 8)..].Trim().Trim('"', '\'');
            var end = cs.IndexOfAny([';', ' ', '"']);
            if (end > 0) cs = cs[..end];
            try { return Encoding.GetEncoding(cs); } catch (ArgumentException) { }
        }
        return Encoding.UTF8;
    }
}
