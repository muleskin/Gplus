namespace Gplus.Mail;

/// <summary>A provider-neutral view of one message, as the classifier sees it.</summary>
public sealed class MailItem
{
    public string Id { get; init; } = "";
    public string ThreadId { get; init; } = "";

    /// <summary>Display name part of From ("Chase Bank" in "Chase Bank &lt;x@y.com&gt;").</summary>
    public string FromName { get; init; } = "";
    public string FromAddress { get; init; } = "";
    public string FromDomain => Addresses.DomainOf(FromAddress);

    public string ReplyToAddress { get; init; } = "";

    /// <summary>Addresses from To + Cc.</summary>
    public IReadOnlyList<string> Recipients { get; init; } = [];

    public string Subject { get; init; } = "";

    /// <summary>Plain text body (HTML already stripped), truncated.</summary>
    public string Body { get; init; } = "";

    public int LinkCount { get; init; }

    /// <summary>Header name → first value, case-insensitive.</summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gmail label ids (INBOX, UNREAD, CATEGORY_PROMOTIONS, STARRED, ...).</summary>
    public IReadOnlyCollection<string> Labels { get; init; } = [];

    public DateTimeOffset? Date { get; init; }

    public string Header(string name) => Headers.TryGetValue(name, out var v) ? v : "";
    public bool HasLabel(string label) => Labels.Contains(label, StringComparer.OrdinalIgnoreCase);
}
